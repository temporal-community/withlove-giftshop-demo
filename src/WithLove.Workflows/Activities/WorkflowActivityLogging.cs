using Microsoft.Extensions.Logging;

namespace WithLove.Workflows.Activities;

internal static partial class WorkflowActivityLogging
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema created successfully")]
    internal static partial void DatabaseSchemaCreated(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database already exists, no changes made")]
    internal static partial void DatabaseAlreadyExists(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ensured full-text catalog and index exist on Products table")]
    internal static partial void EnsuredProductSearchIndexes(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ensured index IX_AspNetUsers_StripeCustomerId exists on AspNetUsers table")]
    internal static partial void EnsuredStripeCustomerIndex(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} categories")]
    internal static partial void SeededCategories(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Categories already exist, skipping category seeding")]
    internal static partial void CategoriesAlreadySeeded(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Stripe product {StripeProductId} / price {StripePriceId} for '{ProductName}'")]
    internal static partial void CreatedStripeProduct(this ILogger logger, string stripeProductId, string stripePriceId, string productName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} products")]
    internal static partial void SeededProducts(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Products already exist, skipping product seeding")]
    internal static partial void ProductsAlreadySeeded(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "All products already have embeddings")]
    internal static partial void ProductsAlreadyEmbedded(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating embeddings for {Count} products")]
    internal static partial void GeneratingEmbeddings(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated embeddings for {Count} products")]
    internal static partial void GeneratedEmbeddings(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolving user ID for Stripe customer {StripeCustomerId}")]
    internal static partial void ResolvingUserByStripeCustomer(this ILogger logger, string stripeCustomerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "No user found for Stripe customer {StripeCustomerId}")]
    internal static partial void NoUserForStripeCustomer(this ILogger logger, string stripeCustomerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolved Stripe customer {StripeCustomerId} to user {UserId}")]
    internal static partial void ResolvedUserByStripeCustomer(this ILogger logger, string stripeCustomerId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ensuring loyalty account and earning {Points} points for user {UserId}, session {StripeSessionId}")]
    internal static partial void EnsuringLoyaltyEarn(this ILogger logger, int points, string userId, string stripeSessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "EarnPoints signal sent: {Points} pts for user {UserId}, session {StripeSessionId}")]
    internal static partial void EarnPointsSignalSent(this ILogger logger, int points, string userId, string stripeSessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Committing loyalty redemption {RedemptionId} for user {UserId}")]
    internal static partial void CommittingRedemption(this ILogger logger, string redemptionId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "CommitRedemption signal sent: redemptionId {RedemptionId}, user {UserId}")]
    internal static partial void CommitRedemptionSignalSent(this ILogger logger, string redemptionId, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to load Love Tokens for user {UserId}")]
    internal static partial void UnableToLoadLoveTokens(this ILogger logger, Exception exception, string? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieving checkout session {SessionId}")]
    internal static partial void RetrievingCheckoutSession(this ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully retrieved checkout session {SessionId}. Amount: {Amount}, Status: {Status}")]
    internal static partial void RetrievedCheckoutSession(this ILogger logger, string sessionId, long? amount, string? status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to retrieve checkout session {SessionId}")]
    internal static partial void FailedToRetrieveCheckoutSession(this ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending order confirmation email to {Email} for order {ConfirmationNumber}")]
    internal static partial void SendingOrderConfirmationEmail(this ILogger logger, string email, string confirmationNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order confirmation email sent to {Email}")]
    internal static partial void OrderConfirmationEmailSent(this ILogger logger, string email);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send confirmation email to {Email}")]
    internal static partial void FailedToSendConfirmationEmail(this ILogger logger, Exception exception, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating order record for checkout session {SessionId}, customer {Email}")]
    internal static partial void CreatingOrderRecord(this ILogger logger, string sessionId, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order processed for {Email}, amount {Amount}")]
    internal static partial void OrderProcessed(this ILogger logger, string email, long amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Order processing failed for session {SessionId}")]
    internal static partial void OrderProcessingFailed(this ILogger logger, string sessionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fulfillment initiated for order {ConfirmationNumber}. Tracking: {Tracking}")]
    internal static partial void FulfillmentInitiated(this ILogger logger, string confirmationNumber, string tracking);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating order {ConfirmationNumber} status to {Status}, tracking {Tracking}")]
    internal static partial void UpdatingOrderStatus(this ILogger logger, string confirmationNumber, string status, string tracking);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {ConfirmationNumber} status updated to {Status}")]
    internal static partial void OrderStatusUpdated(this ILogger logger, string confirmationNumber, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update order {ConfirmationNumber} status")]
    internal static partial void FailedToUpdateOrderStatus(this ILogger logger, Exception exception, string confirmationNumber);
}
