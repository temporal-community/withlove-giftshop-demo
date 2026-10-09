using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WithLove.Web.Services;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "shopSite";
    internal const string ActivitySourceVersion = "1.0.0";

    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public Counter<long> CartOperations { get; }
    public Histogram<long> LoyaltyReserveDuration { get; }
    public Histogram<int> CartItemsAtCheckout { get; }
    public Counter<long> ChatSessionsStarted { get; }
    public Counter<long> ChatCartActions { get; }
    public Histogram<double> ChatTurnDuration { get; }
    public Counter<long> ChatTokensUsed { get; }
    public Histogram<long> ChatTurnTokens { get; }
    public Counter<long> ChatTurnsWithoutUsage { get; }
    public Counter<long> ChatSessionIdentityFailures { get; }

    public Instrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName, ActivitySourceVersion);
        Meter = new Meter(ActivitySourceName, ActivitySourceVersion);

        CartOperations = Meter.CreateCounter<long>(
            "cart.operations",
            description: "Number of cart operations, tagged by operation type");

        LoyaltyReserveDuration = Meter.CreateHistogram<long>(
            "loyalty.reserve.duration_ms",
            unit: "ms",
            description: "Time to execute ReservePointsAsync Temporal Update — blocks checkout UI");

        CartItemsAtCheckout = Meter.CreateHistogram<int>(
            "cart.items_at_checkout",
            description: "Item count in cart at the moment checkout is initiated");

        ChatSessionsStarted = Meter.CreateCounter<long>(
            "chat.session.started",
            description: "Chat sessions opened, by authentication type");

        ChatCartActions = Meter.CreateCounter<long>(
            "chat.message.cart_actions",
            description: "Cart mutations triggered by the AI assistant, by action type");

        ChatTurnDuration = Meter.CreateHistogram<double>(
            "chat.turn.duration_ms",
            unit: "ms",
            description: "End-to-end duration of a durable chat Update");

        // Token spend is the only cost in this application that scales with model behaviour rather
        // than with traffic, and a single turn can make up to 40 model calls under the workflow's
        // tool-iteration cap. The durable AI package already tags raw counts onto each gen_ai span,
        // but spans are sampled and are the wrong shape for a budget alarm — these instruments are
        // the aggregate view: a monotonic counter to bill against and a distribution to alert on.
        ChatTokensUsed = Meter.CreateCounter<long>(
            "chat.turn.tokens",
            unit: "{token}",
            description: "Model tokens billed by durable chat turns, by token type "
                         + "(input/output) and turn completion reason");

        ChatTurnTokens = Meter.CreateHistogram<long>(
            "chat.turn.token_usage",
            unit: "{token}",
            description: "Total tokens consumed by a single durable chat turn — the per-turn "
                         + "distribution a token budget is set against");

        // A turn that fails mid-loop has already burned tokens that never reach the counters above,
        // so cost tracking that silently under-reports is worse than none. This counter makes the
        // blind spot measurable: compare it against chat.turn.duration_ms to size the gap.
        ChatTurnsWithoutUsage = Meter.CreateCounter<long>(
            "chat.turn.usage_unreported",
            description: "Durable chat turns that returned no token usage, by completion reason — "
                         + "tokens these turns spent are absent from chat.turn.tokens");

        // Stable anonymous chat identity is delivered entirely by the wl-chat-id cookie reaching
        // the circuit. If that wiring breaks — middleware unregistered, the persistent-state
        // registration dropped, a consent gate suppressing the cookie — the feature reverts to a
        // fresh workflow per visitor, and the tempting "just mint a GUID" fallback would make that
        // reversion completely invisible. ChatService throws instead, and this counter is what
        // reports it: zero in a healthy system, non-zero exactly when resume has stopped working.
        // A log line is not a report.
        ChatSessionIdentityFailures = Meter.CreateCounter<long>(
            "chat.session.identity_failures",
            description: "Chat sessions that could not be started because the anonymous chat "
                         + "identity was missing — the stable-identity wiring is broken");
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
