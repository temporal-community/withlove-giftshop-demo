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
        Workflow.Logger.LogInformation(
            "Starting order processing workflow for session {SessionId}",
            input.CheckoutSessionId);

        // Step 1: Retrieve checkout session details from Stripe
        var sessionInfo = await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.GetCheckoutSessionAsync(input.CheckoutSessionId),
            StripeActivityOptions);

        Workflow.Logger.LogInformation(
            "Retrieved session for {Email}, amount {Amount}",
            sessionInfo.CustomerEmail,
            sessionInfo.AmountTotal);

        // Step 2: Create order record and initiate fulfillment
        var orderInfo = await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.ProvisionOrderAsync(sessionInfo),
            OrderActivityOptions);

        Workflow.Logger.LogInformation(
            "Order provisioned: {ConfirmationNumber}, tracking: {Tracking}",
            orderInfo.ConfirmationNumber,
            orderInfo.TrackingNumber);

        // Step 3: Update order status to "confirmed"
        var confirmedOrder = orderInfo with { Status = "CONFIRMED" };
        await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) => act.UpdateOrderStatusAsync(confirmedOrder),
            OrderActivityOptions);

        // Step 4: Send confirmation email to customer
        var totalAmount = sessionInfo.AmountTotal / 100m;
        await Workflow.ExecuteActivityAsync(
            (StripeCheckoutOrderActivities act) =>
                act.SendOrderConfirmationEmailAsync(orderInfo.ConfirmationNumber, sessionInfo.CustomerEmail, totalAmount),
            EmailActivityOptions);

        // Step 5: Credit earned points if checkout belongs to a known customer
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
                Workflow.Logger.LogWarning(
                    "Loyalty earn failed for Stripe customer {StripeCustomerId} — order complete",
                    input.StripeCustomerId);
            }
        }

        // Step 6: Commit pending loyalty redemption — uses userId from metadata, NOT from Step 5 lookup.
        // Wrapped in try/catch: a loyalty commit failure must NOT fail a confirmed order workflow.
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
                Workflow.Logger.LogWarning(
                    "Loyalty commit failed for redemption {RedemptionId} — order complete, reservation will expire",
                    input.RedemptionId);
            }
        }

        Workflow.Logger.LogInformation(
            "Order processing workflow completed for {ConfirmationNumber}",
            orderInfo.ConfirmationNumber);
    }
}

public record CheckoutOrderInput(
    string CheckoutSessionId,
    string? StripeCustomerId = null,
    string? RedemptionId = null,
    string? UserId = null);
