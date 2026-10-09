using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace WithLove.Web.Tests.Fakes;

/// <summary>
/// Captures every <c>Append</c> with the <see cref="CookieOptions"/> it was given, and fails a
/// <c>Delete</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Set-Cookie</c> wire format does not carry <c>IsEssential</c> at all and expresses
/// <c>Expires</c> only to whole seconds, so the options object is the only place a cookie's design
/// decisions are fully observable. Assert the header too, but as a secondary check.
/// </para>
/// <para>
/// <c>Delete</c> throws rather than being merely unasserted: writing this application's cookies is
/// a single <c>Append</c>, never a <c>Delete</c> followed by an <c>Append</c>. That much is our own
/// code's contract and is assertable here. What is <em>not</em> assertable here is any claim about
/// what a browser does with two <c>Set-Cookie</c> headers for one name — that is a statement about
/// user agents, and no in-process test can reach it.
/// </para>
/// </remarks>
internal sealed class CapturingResponseCookies : IResponseCookies
{
    public List<(string Name, string Value, CookieOptions Options)> Appended { get; } = [];

    public void Append(string key, string value) => Append(key, value, new CookieOptions());

    public void Append(string key, string value, CookieOptions options) =>
        Appended.Add((key, value, options));

    public void Append(
        ReadOnlySpan<KeyValuePair<string, string>> keyValuePairs,
        CookieOptions options)
    {
        foreach (var pair in keyValuePairs)
            Append(pair.Key, pair.Value, options);
    }

    public void Delete(string key) => throw new InvalidOperationException(
        $"The {key} cookie must be written with a single Append, never deleted first.");

    public void Delete(string key, CookieOptions options) => Delete(key);
}

internal sealed class CapturingResponseCookiesFeature(IResponseCookies cookies)
    : IResponseCookiesFeature
{
    public IResponseCookies Cookies => cookies;
}
