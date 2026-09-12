using WithLove.Web.Telemetry;

namespace WithLove.Web;

internal static class TelemetryIdentityFactory
{
    // SAMPLE ONLY: The committed key keeps raw identifiers out of traces and preserves stable
    // grouping across local and deployed demos. It is public, so it is not a privacy boundary and
    // must not be reused by an application that handles real customers or production data.
    private const string SampleKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string SampleKeyVersion = "demo-v1";

    internal static TelemetryIdentity Create() => TelemetryIdentity.Create(SampleKey, SampleKeyVersion);
}
