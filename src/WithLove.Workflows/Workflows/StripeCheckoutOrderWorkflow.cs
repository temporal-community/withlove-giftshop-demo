using Temporalio.Exceptions;
using Temporalio.Workflows;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Loyalty;

namespace WithLove.Workflows.Workflows;

[Workflow]
public class StripeCheckoutOrderWorkflow
{
    private static readonly ActivityOptions StripeActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromSeconds(30),
            MaximumAttempts = 5
        }
    };

    private static readonly ActivityOptions OrderActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromSeconds(30),
            MaximumAttempts = 3
        }
    };

    private static readonly ActivityOptions EmailActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(1),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(5),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromMinutes(1),
            MaximumAttempts = 5
        }
    };

    // Bounded retries for non-critical infrastructure calls (lookup, resolve).
    private static readonly ActivityOptions LoyaltyActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(1),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromSeconds(30),
            MaximumAttempts = 3
        }
    };

    // Earn and commit both send idempotent Signals to LoyaltyAccountWorkflow on a paid order.
    // Both use unbounded retries — silent loss of a financial mutation is not acceptable.
    private static readonly ActivityOptions LoyaltySignalActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(5),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromMinutes(10)
            // No MaximumAttempts — defaults to unlimited
        }
    };

    [WorkflowRun]
    public async Task RunAsync(CheckoutOrderInput input)
    {
        Workflow.Logger.StartingOrderWorkflow(input.CheckoutSessionId);

        var sessionInfo = await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.GetCheckoutSessionAsync(input.CheckoutSessionId),
            StripeActivityOptions);

        Workflow.Logger.RetrievedSession(sessionInfo.CustomerEmail, sessionInfo.AmountTotal);

        var orderInfo = await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.ProvisionOrderAsync(sessionInfo),
            OrderActivityOptions);

        Workflow.Logger.OrderProvisioned(orderInfo.ConfirmationNumber, orderInfo.TrackingNumber);

        // AmountTotal is in cents (Stripe convention). Tagged to reveal loyalty programme lift.
        Workflow.MetricMeter.CreateHistogram<long>("order.revenue", unit: "cents")
            .Record(sessionInfo.AmountTotal, new[]
            {
                new KeyValuePair<string, object>(
                    "loyalty_redemption_applied",
                    !string.IsNullOrEmpty(input.RedemptionId)),
            });

        var confirmedOrder = orderInfo with { Status = "CONFIRMED" };
        await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.UpdateOrderStatusAsync(confirmedOrder),
            OrderActivityOptions);

        // Step 4: Confirmation email — best-effort; exhausted retries must not block loyalty.
        var totalAmount = sessionInfo.AmountTotal / 100m;
        try
        {
            await Workflow.ExecuteActivityAsync(
                (StripeCheckoutOrderActivities act) =>
                    act.SendOrderConfirmationEmailAsync(orderInfo.ConfirmationNumber, sessionInfo.CustomerEmail, totalAmount),
                EmailActivityOptions);
        }
        catch (ActivityFailureException)
        {
            Workflow.Logger.EmailConfirmationFailed(orderInfo.ConfirmationNumber);
        }

        // Step 5: Credit earned points.
        // Primary: UserId from session metadata — always set for authenticated checkouts (Checkout.razor is [Authorize]).
        // Fallback: resolve via StripeCustomerId if UserId is absent — defensive only; handles
        // replayed webhooks, manual event injection, or events predating the metadata field.
        var earnUserId = input.UserId;
        if (string.IsNullOrEmpty(earnUserId) && !string.IsNullOrEmpty(input.StripeCustomerId))
        {
            try
            {
                earnUserId = await Workflow.ExecuteActivityAsync(
                    (LoyaltyActivities act) => act.ResolveUserIdByStripeCustomerIdAsync(input.StripeCustomerId),
                    LoyaltyActivityOptions);
            }
            catch (ActivityFailureException)
            {
                Workflow.Logger.LoyaltyEarnFailed(input.StripeCustomerId);
            }
        }

        if (!string.IsNullOrEmpty(earnUserId))
        {
            // Unbounded retries — earn is an idempotent Signal on a paid order.
            // Same durability guarantee as commit: silent loss is not acceptable.
            var points = (int)(sessionInfo.AmountTotal / 100); // $1 = 1 point (net, after coupons)
            await Workflow.ExecuteActivityAsync(
                (LoyaltyActivities act) => act.EnsureAndEarnPointsAsync(
                    new EnsureLoyaltyInput(earnUserId, sessionInfo.SessionId, orderInfo.ConfirmationNumber, points)),
                LoyaltySignalActivityOptions);
        }

        // Step 6: Commit pending redemption — unbounded retries, no catch.
        // CommitRedemptionAsync is idempotent. 24h reservation expiry is the last-resort safety net.
        if (!string.IsNullOrEmpty(input.UserId) && !string.IsNullOrEmpty(input.RedemptionId))
        {
            await Workflow.ExecuteActivityAsync(
                (LoyaltyActivities act) => act.CommitRedemptionAsync(input.UserId, input.RedemptionId),
                LoyaltySignalActivityOptions);
        }

        Workflow.Logger.OrderWorkflowCompleted(orderInfo.ConfirmationNumber);
    }
}

public record CheckoutOrderInput(
    string CheckoutSessionId,
    string? StripeCustomerId = null,
    string? RedemptionId = null,
    string? UserId = null);
