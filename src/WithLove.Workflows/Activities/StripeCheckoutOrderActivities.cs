using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using Temporalio.Activities;

namespace WithLove.Workflows.Activities;

/// <summary>
/// Activities for processing Stripe checkout orders.
/// Each activity represents a single, durable unit of work that can be retried independently.
/// </summary>
public partial class StripeCheckoutOrderActivities(StripeClient stripeClient)
{
    /// <summary>
    /// Retrieves the completed checkout session from Stripe.
    /// Includes customer, shipping, and payment details.
    /// </summary>
    [Activity]
    public async Task<CheckoutSessionInfo> GetCheckoutSessionAsync(string sessionId)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Retrieving checkout session {SessionId}", sessionId);

        try
        {
            var options = new SessionGetOptions
            {
                Expand = ["payment_intent", "customer"]
            };
            var session = await stripeClient.V1.Checkout.Sessions.GetAsync(sessionId, options);

            logger.LogInformation(
                "Successfully retrieved checkout session {SessionId}. Amount: {Amount}, Status: {Status}",
                sessionId,
                session.AmountTotal,
                session.PaymentStatus);

            return new CheckoutSessionInfo(session.Id, session.CustomerEmail, session.PaymentIntent.Amount,
                session.PaymentIntent.Id);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Failed to retrieve checkout session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Sends an order confirmation email to the customer.
    /// In a real app, this would use an email service (SendGrid, AWS SES, etc.).
    /// </summary>
    [Activity]
    public async Task SendOrderConfirmationEmailAsync(string confirmationNumber, string customerEmail,
        decimal totalAmount)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation(
            "Sending order confirmation email to {Email} for order {ConfirmationNumber}",
            customerEmail,
            confirmationNumber);

        try
        {
            await Task.Delay(200); // Simulate email send 
            logger.LogInformation("Order confirmation email sent to {Email}", customerEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send confirmation email to {Email}", customerEmail);
            throw;
        }
    }

    /// <summary>
    /// Creates an order record in the application database.
    /// In a real app, this would persist order data, line items, and customer info.
    /// </summary>
    [Activity]
    public async Task<OrderInfo> ProvisionOrderAsync(CheckoutSessionInfo sessionInfo)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation(
            "Creating order record for checkout session {SessionId}, customer {Email}",
            sessionInfo.SessionId,
            sessionInfo.CustomerEmail);

        try
        {
            var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionInfo.SessionId);

            LogOrderProcessed(logger, sessionInfo.CustomerEmail, sessionInfo.AmountTotal);

            await Task.Delay(500); // Simulate work

            var trackingNumber = $"TRK{DateTime.UtcNow.Ticks % 1000000000:D9}";

            logger.LogInformation(
                "Fulfillment initiated for order {ConfirmationNumber}. Tracking: {Tracking}",
                confirmationNumber,
                trackingNumber);

            return new OrderInfo(confirmationNumber, trackingNumber, "READY_TO_SHIP");
        }
        catch (Exception ex)
        {
            LogOrderFailed(logger, sessionInfo.SessionId, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order processed for {Email}, amount {Amount}")]
    private static partial void LogOrderProcessed(ILogger logger, string email, long amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Order processing failed for session {SessionId}")]
    private static partial void LogOrderFailed(ILogger logger, string sessionId, Exception ex);

    /// <summary>
    /// Updates the order status and stores fulfillment details.
    /// </summary>
    [Activity]
    public async Task UpdateOrderStatusAsync(OrderInfo orderInfo)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation(
            "Updating order {ConfirmationNumber} status to {Status}, tracking {Tracking}",
            orderInfo.ConfirmationNumber,
            orderInfo.Status,
            orderInfo.TrackingNumber);

        try
        {
            await Task.Delay(100); // Simulate database update
            logger.LogInformation("Order {ConfirmationNumber} status updated to {Status}", orderInfo.ConfirmationNumber,
                orderInfo.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update order {ConfirmationNumber} status", orderInfo.ConfirmationNumber);
            throw;
        }
    }
}


public record CheckoutSessionInfo(
    string SessionId,
    string CustomerEmail,
    long AmountTotal,
    string PaymentIntentId);


public record OrderInfo(string ConfirmationNumber, string TrackingNumber, string Status)
{
    public static string GenerateConfirmationNumber(string sessionId)
    {
        var suffix = sessionId.Length >= 7 ? sessionId[^7..] : sessionId;
        return $"#WL-{suffix.ToUpperInvariant()}";
    }
}