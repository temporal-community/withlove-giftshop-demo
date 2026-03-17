using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using WithLove.Workflows.Activities;

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

        Workflow.Logger.LogInformation(
            "Order processing workflow completed for {ConfirmationNumber}",
            orderInfo.ConfirmationNumber);
    }
}

public record CheckoutOrderInput(string CheckoutSessionId);
