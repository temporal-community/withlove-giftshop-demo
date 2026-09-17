using System.Diagnostics;
using OpenTelemetry;

namespace WithLove.ServiceDefaults.Telemetry;

/// <summary>
/// Carries an opaque chat-turn root through distributed execution for AI-only trace export.
/// </summary>
/// <remarks>
/// The baggage value contains only a trace ID and span ID. It is intentionally not an application
/// identity, workflow ID, session ID, or user ID. Restoring the complete prior baggage prevents one
/// chat turn's root from leaking into the next turn on the same asynchronous execution context.
/// </remarks>
public static class AiTraceAnchorScope
{
    internal const string BaggageKey = "withlove.ai.trace_root";

    /// <summary>Publishes an activity as the current distributed AI trace root.</summary>
    /// <param name="activity">The application-owned chat-turn activity.</param>
    /// <returns>A scope that restores the preceding baggage when disposed.</returns>
    public static IDisposable Push(Activity? activity)
    {
        if (activity is null)
            return NoopScope.Instance;

        var previous = Baggage.Current;
        var value = $"{activity.TraceId.ToHexString()}:{activity.SpanId.ToHexString()}";
        // .NET's HttpClient propagates Activity baggage itself on modern runtimes. Keep this
        // synchronized with OpenTelemetry.Baggage, which the Temporal interceptor serializes.
        activity.SetBaggage(BaggageKey, value);
        Baggage.SetBaggage(BaggageKey, value);
        return new RestoreBaggageScope(previous);
    }

    internal static bool TryGetCurrent(out AiTraceAnchor anchor)
    {
        var value = Baggage.GetBaggage(BaggageKey);
        if (value is null)
        {
            anchor = default;
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator != 32 || value.Length != 49)
        {
            anchor = default;
            return false;
        }

        var traceId = value[..separator];
        var spanId = value[(separator + 1)..];
        if (!IsLowerHex(traceId) || !IsLowerHex(spanId))
        {
            anchor = default;
            return false;
        }

        anchor = new AiTraceAnchor(traceId, spanId);
        return true;
    }

    private static bool IsLowerHex(string value)
    {
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    private sealed class RestoreBaggageScope(Baggage previous) : IDisposable
    {
        private Baggage? previous = previous;

        public void Dispose()
        {
            if (previous is { } value)
            {
                Baggage.Current = value;
                previous = null;
            }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal readonly record struct AiTraceAnchor(string TraceId, string SpanId);
