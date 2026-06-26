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
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
