using Microsoft.Extensions.Logging;
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

    private static readonly ActivityOptions LoyaltyActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(1),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            MaximumAttempts = 3
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

        var confirmedOrder = orderInfo with { Status = "CONFIRMED" };
        await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.UpdateOrderStatusAsync(confirmedOrder),
            OrderActivityOptions);

        var totalAmount = sessionInfo.AmountTotal / 100m;
        await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) =>
                act.SendOrderConfirmationEmailAsync(orderInfo.ConfirmationNumber, sessionInfo.CustomerEmail, totalAmount),
            EmailActivityOptions);

        if (!string.IsNullOrEmpty(input.StripeCustomerId))
        {
            try
            {
                var userId = await Workflow.ExecuteActivityAsync(
                    (LoyaltyActivities act) => act.ResolveUserIdByStripeCustomerIdAsync(input.StripeCustomerId),
                    LoyaltyActivityOptions);

                if (userId is not null)
                {
                    var points = (int)(sessionInfo.AmountTotal / 100); // $1 = 1 point
                    await Workflow.ExecuteActivityAsync(
                        (LoyaltyActivities act) => act.EnsureAndEarnPointsAsync(
                            new EnsureLoyaltyInput(userId, sessionInfo.SessionId, orderInfo.ConfirmationNumber, points)),
                        LoyaltyActivityOptions);
                }
            }
            catch (ActivityFailureException)
            {
                Workflow.Logger.LoyaltyEarnFailed(input.StripeCustomerId);
            }
        }

        // Commit by metadata userId; do not fail a paid order if loyalty is down.
        if (!string.IsNullOrEmpty(input.UserId) && !string.IsNullOrEmpty(input.RedemptionId))
        {
            try
            {
                await Workflow.ExecuteActivityAsync(
                    (LoyaltyActivities act) => act.CommitRedemptionAsync(input.UserId, input.RedemptionId),
                    LoyaltyActivityOptions);
            }
            catch (ActivityFailureException)
            {
                Workflow.Logger.LoyaltyCommitFailed(input.RedemptionId);
            }
        }

        Workflow.Logger.OrderWorkflowCompleted(orderInfo.ConfirmationNumber);
    }
}

public record CheckoutOrderInput(
    string CheckoutSessionId,
    string? StripeCustomerId = null,
    string? RedemptionId = null,
    string? UserId = null);
