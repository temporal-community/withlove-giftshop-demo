using Microsoft.Extensions.Logging;

namespace WithLove.Workflows.Workflows;

internal static partial class WorkflowLogging
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting database setup workflow")]
    internal static partial void StartingDatabaseSetup(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration complete: {Count} applied — {Message}")]
    internal static partial void MigrationComplete(this ILogger logger, int count, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema upgrades applied successfully")]
    internal static partial void SchemaUpgradesApplied(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding complete: {Categories} categories, {Products} products")]
    internal static partial void SeedingComplete(this ILogger logger, int categories, int products);

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedding generation complete: {Count} products embedded")]
    internal static partial void EmbeddingGenerationComplete(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting onboarding workflow for customer {CustomerId}")]
    internal static partial void StartingOnboarding(this ILogger logger, string customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed onboarding workflow for customer {CustomerId}")]
    internal static partial void CompletedOnboarding(this ILogger logger, string customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting order processing workflow for session {SessionId}")]
    internal static partial void StartingOrderWorkflow(this ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved session for {Email}, amount {Amount}")]
    internal static partial void RetrievedSession(this ILogger logger, string email, long amount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order provisioned: {ConfirmationNumber}, tracking: {Tracking}")]
    internal static partial void OrderProvisioned(this ILogger logger, string confirmationNumber, string tracking);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loyalty earn failed for Stripe customer {StripeCustomerId} — order complete")]
    internal static partial void LoyaltyEarnFailed(this ILogger logger, string stripeCustomerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loyalty commit failed for redemption {RedemptionId} — order complete, reservation will expire")]
    internal static partial void LoyaltyCommitFailed(this ILogger logger, string redemptionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order processing workflow completed for {ConfirmationNumber}")]
    internal static partial void OrderWorkflowCompleted(this ILogger logger, string confirmationNumber);
}
