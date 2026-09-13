using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Stripe;

namespace WithLove.StripeWebhooks.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for the <c>/v1/webhook_endpoints</c> resource, injected through
/// <see cref="StripeClientOptions.HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No test in this project may call the real Stripe API.</b> The sandbox account is shared and
/// is kept at zero endpoints; a test that creates one leaves debris that the 16-endpoint cap
/// eventually turns into a failing deploy for somebody else.
/// </para>
/// <para>
/// This fake holds state rather than replaying canned responses because the create path is a
/// sequence — list, create, update (fingerprint), list again — and each step must observe the
/// effects of the last. A stateless fake would let a bug in that ordering pass. It also parses the
/// real form-encoded request bodies the SDK produces, so assertions about <c>api_version</c> or
/// <c>metadata[...]</c> are assertions about what would actually go over the wire.
/// </para>
/// </remarks>
internal sealed class FakeStripeAccount : IHttpClient
{
    private const string ResourcePath = "/v1/webhook_endpoints";

    private readonly List<FakeEndpoint> _endpoints = [];
    private int _nextId = 1;

    /// <summary>Every request the SDK made, in order, as "METHOD /path".</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Form bodies of every mutating request, in order.</summary>
    public List<IReadOnlyDictionary<string, string>> Bodies { get; } = [];

    /// <summary>
    /// Set to make the next matching request fail with a Stripe API error, so failure branches are
    /// exercised rather than described.
    /// </summary>
    public Func<HttpMethod, string, bool>? FailWhen { get; set; }

    public IReadOnlyList<FakeEndpoint> Endpoints => _endpoints;

    public FakeEndpoint Seed(
        string url,
        IDictionary<string, string>? metadata = null,
        string? description = null,
        IEnumerable<string>? events = null,
        string status = "enabled")
    {
        var endpoint = new FakeEndpoint
        {
            Id = $"we_seed_{_nextId++}",
            Url = url,
            Description = description ?? "seeded",
            Events = [.. events ?? ["checkout.session.completed", "checkout.session.expired"]],
            Metadata = metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            Status = status,
            Secret = $"whsec_seeded{_nextId}",
        };

        _endpoints.Add(endpoint);
        return endpoint;
    }

    public async Task<StripeResponse> MakeRequestAsync(
        StripeRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = request.Uri.AbsolutePath;
        Requests.Add($"{request.Method} {path}");

        var body = request.Content is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ParseForm(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (request.Content is not null)
            Bodies.Add(body);

        if (FailWhen?.Invoke(request.Method, path) == true)
            return Response(HttpStatusCode.BadRequest, ErrorJson("invalid_request_error", "injected failure"));

        if (path == ResourcePath && request.Method == HttpMethod.Get)
            return Response(HttpStatusCode.OK, ListJson());

        if (path == ResourcePath && request.Method == HttpMethod.Post)
            return Response(HttpStatusCode.OK, Create(body));

        if (path.StartsWith(ResourcePath + "/", StringComparison.Ordinal))
        {
            var id = path[(ResourcePath.Length + 1)..];
            var endpoint = _endpoints.FirstOrDefault(candidate => candidate.Id == id);
            if (endpoint is null)
            {
                return Response(
                    HttpStatusCode.NotFound,
                    ErrorJson("invalid_request_error", $"No such webhook endpoint: {id}"));
            }

            if (request.Method == HttpMethod.Delete)
            {
                _endpoints.Remove(endpoint);
                return Response(
                    HttpStatusCode.OK,
                    $$"""{"id":"{{id}}","object":"webhook_endpoint","deleted":true}""");
            }

            if (request.Method == HttpMethod.Post)
            {
                Update(endpoint, body);
                return Response(HttpStatusCode.OK, Serialize(endpoint, includeSecret: false));
            }
        }

        throw new InvalidOperationException(
            $"FakeStripeAccount received an unexpected request: {request.Method} {request.Uri}");
    }

    /// <summary>
    /// Streaming is only used by Stripe's file-download APIs, which this tool never touches.
    /// Throwing is better than returning an empty stream: if a future change starts streaming, the
    /// test fails loudly instead of silently asserting against nothing.
    /// </summary>
    public Task<StripeStreamedResponse> MakeStreamingRequestAsync(
        StripeRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeStripeAccount does not serve streaming requests.");

    private string Create(IReadOnlyDictionary<string, string> body)
    {
        var endpoint = new FakeEndpoint
        {
            Id = $"we_created_{_nextId++}",
            Url = body.GetValueOrDefault("url", string.Empty),
            Description = body.GetValueOrDefault("description", string.Empty),
            ApiVersion = body.GetValueOrDefault("api_version"),
            Events = Indexed(body, "enabled_events"),
            Metadata = Mapped(body, "metadata"),
            Status = "enabled",

            // The signing secret is produced here and only here, exactly as Stripe does it: the
            // create response is the one and only time it is observable.
            Secret = $"whsec_{Guid.NewGuid():N}",
        };

        _endpoints.Add(endpoint);
        return Serialize(endpoint, includeSecret: true);
    }

    private static void Update(FakeEndpoint endpoint, IReadOnlyDictionary<string, string> body)
    {
        if (body.TryGetValue("url", out var url))
            endpoint.Url = url;

        if (body.TryGetValue("description", out var description))
            endpoint.Description = description;

        if (body.Keys.Any(key => key.StartsWith("enabled_events[", StringComparison.Ordinal)))
            endpoint.Events = Indexed(body, "enabled_events");

        // Stripe replaces the whole metadata map on update. Reproducing that is the point: it is
        // what makes a non-merging reconcile destroy the secret fingerprint.
        if (body.Keys.Any(key => key.StartsWith("metadata[", StringComparison.Ordinal)))
            endpoint.Metadata = Mapped(body, "metadata");

        if (body.TryGetValue("disabled", out var disabled))
            endpoint.Status = disabled == "true" ? "disabled" : "enabled";
    }

    private string ListJson()
    {
        var items = string.Join(",", _endpoints.Select(endpoint => Serialize(endpoint, includeSecret: false)));
        return $$"""
            {"object":"list","url":"/v1/webhook_endpoints","has_more":false,"data":[{{items}}]}
            """;
    }

    private static string Serialize(FakeEndpoint endpoint, bool includeSecret)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = endpoint.Id,
            ["object"] = "webhook_endpoint",
            ["api_version"] = endpoint.ApiVersion,
            ["created"] = 1_700_000_000,
            ["description"] = endpoint.Description,
            ["enabled_events"] = endpoint.Events,
            ["livemode"] = false,
            ["metadata"] = endpoint.Metadata,
            ["status"] = endpoint.Status,
            ["url"] = endpoint.Url,
        };

        if (includeSecret)
            payload["secret"] = endpoint.Secret;

        return JsonSerializer.Serialize(payload);
    }

    private static string ErrorJson(string type, string message)
        => JsonSerializer.Serialize(new
        {
            error = new { type, message },
        });

    private static StripeResponse Response(HttpStatusCode status, string json)
    {
        HttpResponseHeaders headers = new HttpResponseMessage().Headers;
        headers.TryAddWithoutValidation("Request-Id", "req_fake_stripe_webhooks");
        return new StripeResponse(status, headers, json);
    }

    /// <summary>Collects <c>name[0]</c>, <c>name[1]</c>… back into a list, in index order.</summary>
    private static List<string> Indexed(IReadOnlyDictionary<string, string> body, string name)
        => [.. body
            .Where(pair => pair.Key.StartsWith(name + "[", StringComparison.Ordinal))
            .OrderBy(pair => int.Parse(pair.Key[(name.Length + 1)..^1]))
            .Select(pair => pair.Value)];

    /// <summary>Collects <c>name[key]</c> back into a map.</summary>
    private static Dictionary<string, string> Mapped(IReadOnlyDictionary<string, string> body, string name)
        => body
            .Where(pair => pair.Key.StartsWith(name + "[", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[(name.Length + 1)..^1], pair => pair.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> ParseForm(string content)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(content))
            return parsed;

        foreach (var pair in content.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            parsed[Decode(key)] = Decode(value);
        }

        return parsed;
    }

    private static string Decode(string value)
        => Uri.UnescapeDataString(value.Replace('+', ' '));

    /// <summary>One webhook endpoint as the fake account holds it.</summary>
    internal sealed class FakeEndpoint
    {
        public required string Id { get; init; }

        public required string Url { get; set; }

        public required string Description { get; set; }

        public string? ApiVersion { get; init; }

        public required List<string> Events { get; set; }

        public required Dictionary<string, string> Metadata { get; set; }

        public required string Status { get; set; }

        public required string Secret { get; init; }
    }

    /// <summary>A client wired to this fake account, with a well-formed test key.</summary>
    public IStripeClient CreateClient()
        => new StripeClient(new StripeClientOptions
        {
            ApiKey = "sk_test_fake_for_stripe_webhook_tool_tests",
            HttpClient = this,
        });

    /// <summary>The form body of the most recent mutating request.</summary>
    public IReadOnlyDictionary<string, string> LastBody => Bodies[^1];
}
