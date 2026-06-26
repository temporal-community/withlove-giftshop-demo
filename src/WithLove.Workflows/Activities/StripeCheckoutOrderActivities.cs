using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using Temporalio.Activities;

namespace WithLove.Workflows.Activities;

/// <summary>Retryable activities for Stripe checkout order processing.</summary>
public partial class StripeCheckoutOrderActivities(StripeClient stripeClient)
{
    [Activity]
    public async Task<CheckoutSessionInfo> GetCheckoutSessionAsync(string sessionId)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.RetrievingCheckoutSession(sessionId);

        try
        {
            var options = new SessionGetOptions
            {
                Expand = ["payment_intent", "customer"]
            };
            var session = await stripeClient.V1.Checkout.Sessions.GetAsync(sessionId, options);

            logger.RetrievedCheckoutSession(sessionId, session.AmountTotal, session.PaymentStatus);

            return new CheckoutSessionInfo(session.Id, session.CustomerEmail, session.PaymentIntent.Amount,
                session.PaymentIntent.Id);
        }
        catch (StripeException ex)
        {
            logger.FailedToRetrieveCheckoutSession(ex, sessionId);
            throw;
        }
    }

    [Activity]
    public async Task SendOrderConfirmationEmailAsync(string confirmationNumber, string customerEmail,
        decimal totalAmount)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.SendingOrderConfirmationEmail(customerEmail, confirmationNumber);

        try
        {
            await Task.Delay(200);
            logger.OrderConfirmationEmailSent(customerEmail);
        }
        catch (Exception ex)
        {
            logger.FailedToSendConfirmationEmail(ex, customerEmail);
            throw;
        }
    }

    [Activity]
    public async Task<OrderInfo> ProvisionOrderAsync(CheckoutSessionInfo sessionInfo)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.CreatingOrderRecord(sessionInfo.SessionId, sessionInfo.CustomerEmail);

        try
        {
            var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionInfo.SessionId);

            logger.OrderProcessed(sessionInfo.CustomerEmail, sessionInfo.AmountTotal);

            await Task.Delay(500);

            var trackingNumber = $"TRK{DateTime.UtcNow.Ticks % 1000000000:D9}";

            logger.FulfillmentInitiated(confirmationNumber, trackingNumber);

            return new OrderInfo(confirmationNumber, trackingNumber, "READY_TO_SHIP");
        }
        catch (Exception ex)
        {
            logger.OrderProcessingFailed(sessionInfo.SessionId, ex);
            throw;
        }
    }

    [Activity]
    public async Task UpdateOrderStatusAsync(OrderInfo orderInfo)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.UpdatingOrderStatus(orderInfo.ConfirmationNumber, orderInfo.Status, orderInfo.TrackingNumber);

        try
        {
            await Task.Delay(100);
            logger.OrderStatusUpdated(orderInfo.ConfirmationNumber, orderInfo.Status);
        }
        catch (Exception ex)
        {
            logger.FailedToUpdateOrderStatus(ex, orderInfo.ConfirmationNumber);
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
        return $"WL-{suffix.ToUpperInvariant()}";
    }
}
