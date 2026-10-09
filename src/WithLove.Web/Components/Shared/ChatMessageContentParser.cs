using System.Globalization;
using System.Text.RegularExpressions;
using WithLove.Web.Models;

namespace WithLove.Web.Components.Shared;

/// <summary>
/// Projects assistant product prose into display-safe chat segments.
/// </summary>
/// <remarks>
/// Product tools return IDs and other protocol fields so the model can perform follow-up calls.
/// Those fields are deliberately consumed here rather than rendered as chat markup. The parser is
/// intentionally small and tolerant of the model's usual product formatting because the chat
/// transcript is model-authored text, not an API response. Images are never projected from that
/// text; the UI resolves recognized products against the catalog and owns their presentation.
/// </remarks>
public static partial class ChatMessageContentParser
{
    private const int MaxDescriptionLength = 180;

    public enum SegmentType
    {
        Text,
        Product,
    }

    public sealed record ProductCard(
        string Name,
        decimal Price,
        string Description);

    public sealed record Segment(
        SegmentType Type,
        string Content,
        ProductCard? Product = null);

    /// <summary>Finds the catalog product represented by a model-authored product heading.</summary>
    public static Product? FindCatalogProduct(ProductCard parsed, IEnumerable<Product> catalog)
    {
        var parsedName = NormalizeName(parsed.Name);
        return catalog
            .OrderByDescending(item => NormalizeName(item.Name).Length)
            .FirstOrDefault(item =>
            {
                var catalogName = NormalizeName(item.Name);
                return catalogName == parsedName || parsedName.Contains(catalogName, StringComparison.Ordinal);
            });
    }

    [GeneratedRegex(
        "(?m)^[ \\t]*(?:[-*•]\\s+)?(?<name>[^$\\r\\n]+?)\\s*(?:\\(\\s*(?:product\\s*)?id\\s*:?\\s*\\d+\\s*\\))?\\s*(?:[—–-]|\\|)\\s*\\$(?<price>\\d+(?:\\.\\d{1,2})?)\\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProductHeadingRegex();

    [GeneratedRegex(
        "(?im)^[ \\t]*(?:[-*•]\\s+)?(?:\\*{0,2})?Product(?:\\s+name)?\\s*:(?:\\*{0,2})?\\s*(?<name>[^\\r\\n]+?)\\s*\\r?\\n[ \\t]*(?:[-*•]\\s+)?(?:\\*{0,2})?Price\\s*:(?:\\*{0,2})?\\s*\\$(?<price>\\d+(?:\\.\\d{1,2})?)\\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabeledProductHeadingRegex();

    [GeneratedRegex(
        "\\b(?:has\\s+been|was)\\s+added\\s+to\\s+(?:your\\s+|the\\s+)?cart\\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex CartConfirmationRegex();

    [GeneratedRegex(
        "(?<markdown>!?\\[(?<alt>[^\\]]*)\\]\\((?<url>https?://[^)\\s]+)(?:\\s+[^)]*)?\\))",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(
        "^(https?://\\S+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneUrlRegex();

    [GeneratedRegex(
        "\\s*\\((?:product\\s*)?id\\s*:?\\s*\\d+\\)\\s*",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProductIdRegex();

    [GeneratedRegex(
        "\\b(?:product\\s+)?id\\s*:?\\s*\\d+\\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex InlineProductIdRegex();

    [GeneratedRegex(
        "^\\s*(?:product\\s+)?(?:name|price|image|collection|materials|story|description|product\\s+id)\\s*:\\s*.*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SystemDetailLineRegex();

    [GeneratedRegex(
        "^\\s*(?:why\\s+it(?:'|’)s\\s+special|why\\s+this\\s+works|description)\\s*:\\s*",
        RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionLabelRegex();

    /// <summary>Parses assistant text into ordinary prose and display-only product references.</summary>
    public static IReadOnlyList<Segment> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [new Segment(SegmentType.Text, string.Empty)];

        var segments = new List<Segment>();
        var headings = ProductHeadingRegex().Matches(text).Cast<Match>()
            .Concat(LabeledProductHeadingRegex().Matches(text).Cast<Match>())
            .OrderBy(match => match.Index)
            .ToArray();
        var lastIndex = 0;

        for (var index = 0; index < headings.Length; index++)
        {
            var heading = headings[index];
            AddTextSegment(segments, text[lastIndex..heading.Index]);

            var end = index + 1 < headings.Length ? headings[index + 1].Index : text.Length;
            var block = text[heading.Index..end];

            // A confirmation such as "Velvet Crimson — $89.00 has been added to your cart" is
            // conversational prose, not a recommendation. Rendering it as a compact product card
            // hides the action the customer just asked us to perform.
            if (CartConfirmationRegex().IsMatch(block))
            {
                AddTextSegment(segments, block);
                lastIndex = end;
                continue;
            }

            var product = ParseProduct(heading, block);

            if (product is null)
            {
                AddTextSegment(segments, block);
            }
            else
            {
                segments.Add(new Segment(
                    SegmentType.Product,
                    FormatProductFallback(product),
                    product));
            }

            lastIndex = end;
        }

        AddTextSegment(segments, text[lastIndex..]);
        return segments.Count == 0
            ? [new Segment(SegmentType.Text, CleanText(text))]
            : segments;
    }

    private static ProductCard? ParseProduct(Match heading, string block)
    {
        if (!decimal.TryParse(
                heading.Groups["price"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var price))
        {
            return null;
        }

        var name = ProductIdRegex().Replace(heading.Groups["name"].Value, string.Empty).Trim();
        name = Regex.Replace(name, @"^\s*(?:product\s*:\s*)", string.Empty, RegexOptions.IgnoreCase);
        name = TrimDecorators(name);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var image = MarkdownImageRegex().Match(block);
        var descriptionText = block[heading.Length..];
        if (image.Success)
            descriptionText = descriptionText.Replace(image.Groups["markdown"].Value, string.Empty);

        var description = CleanDescription(descriptionText);
        return new ProductCard(name, price, description);
    }

    private static void AddTextSegment(List<Segment> segments, string text)
    {
        var cleaned = CleanText(text);
        if (!string.IsNullOrWhiteSpace(cleaned))
            segments.Add(new Segment(SegmentType.Text, cleaned));
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Model-authored URLs are protocol text, never presentation input. Product images and
        // links are supplied by the canonical catalog product rendered by ProductCardGrid.
        var withoutMarkdownImages = MarkdownImageRegex().Replace(text, string.Empty);
        var lines = withoutMarkdownImages.Split('\n')
            .Where(line => !StandaloneUrlRegex().IsMatch(line.Trim()))
            .Where(line => !SystemDetailLineRegex().IsMatch(line))
            .Select(line => InlineProductIdRegex().Replace(line, string.Empty))
            .ToArray();
        return string.Join('\n', lines).Trim();
    }

    private static string CleanDescription(string text)
    {
        var lines = text.Split('\n')
            .Select(line => MarkdownImageRegex().Replace(line, string.Empty))
            .Where(line => !SystemDetailLineRegex().IsMatch(line))
            .Select(line => DescriptionLabelRegex().Replace(line, string.Empty))
            .Select(line => Regex.Replace(line, @"^\s*image\s*:\s*|\s+image\s*:\s*$", string.Empty, RegexOptions.IgnoreCase))
            .Select(line => Regex.Replace(line, @"\bdescription\s*:\s*", string.Empty, RegexOptions.IgnoreCase))
            .Select(line => InlineProductIdRegex().Replace(line, string.Empty))
            .Select(line => Regex.Replace(line, @"https?://\S+", string.Empty, RegexOptions.IgnoreCase))
            .Select(line => Regex.Replace(line, @"[*_`#]", string.Empty))
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return LimitDescription(Regex.Replace(string.Join(' ', lines), @"\s+", " ").Trim());
    }

    private static string LimitDescription(string text)
    {
        if (text.Length <= MaxDescriptionLength)
            return text;

        var cutoff = text[..MaxDescriptionLength].LastIndexOf(' ');
        return $"{text[..(cutoff > 0 ? cutoff : MaxDescriptionLength)].TrimEnd()}…";
    }

    private static string FormatProductFallback(ProductCard product)
    {
        var heading = $"**{product.Name}** — ${product.Price:F2}";
        return string.IsNullOrWhiteSpace(product.Description)
            ? heading
            : $"{heading}\n{product.Description}";
    }

    private static string NormalizeName(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string TrimDecorators(string value) =>
        value.Trim().TrimStart('-', '*', '•').Trim();
}
