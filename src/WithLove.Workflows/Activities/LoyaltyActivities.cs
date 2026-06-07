using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using WithLove.Data;
using WithLove.Workflows.Loyalty;
using WithLove.Workflows.Workflows;

namespace WithLove.Workflows.Activities;

/// <summary>
/// Activities for the Love Tokens loyalty points system.
/// IMPORTANT: ITemporalClient is NOT constructor-injected here.
/// AddHostedTemporalWorker (3-arg overload) owns its own internal client and does NOT register
/// ITemporalClient in the DI container. Use ActivityExecutionContext.Current.TemporalClient
/// inside each method body instead. ProductsDbContext and other services remain constructor-injected.
/// </summary>
public class LoyaltyActivities(ProductsDbContext dbContext)
{
    /// <summary>
    /// Resolves the application user ID for a given Stripe customer ID.
    /// Returns null if no user is found — callers should treat null as "loyalty earn not applicable".
    /// NOTE: A raw SQL index on AspNetUsers(StripeCustomerId) should be created in
    /// ApplySchemaUpgradesAsync — EF model config alone is insufficient since the project does
    /// not use EF Core migrations:
    ///   IF NOT EXISTS (
    ///     SELECT 1 FROM sys.indexes
    ///     WHERE name = 'IX_AspNetUsers_StripeCustomerId'
    ///       AND object_id = OBJECT_ID('AspNetUsers')
    ///   )
    ///   BEGIN
    ///     CREATE INDEX IX_AspNetUsers_StripeCustomerId ON AspNetUsers(StripeCustomerId);
    ///   END
    /// </summary>
    [Activity]
    public async Task<string?> ResolveUserIdByStripeCustomerIdAsync(string stripeCustomerId)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation(
            "Resolving user ID for Stripe customer {StripeCustomerId}", stripeCustomerId);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId);

        if (user is null)
        {
            logger.LogInformation(
                "No user found for Stripe customer {StripeCustomerId}", stripeCustomerId);
            return null;
        }

        logger.LogInformation(
            "Resolved Stripe customer {StripeCustomerId} to user {UserId}",
            stripeCustomerId, user.Id);

        return user.Id;
    }

    /// <summary>
    /// Lazily starts the loyalty workflow for the user (using IdConflictPolicy.UseExisting)
    /// and sends an EarnPoints signal to credit the purchase amount.
    /// The StripeSessionId is the idempotency key — sending the same signal twice is safe.
    /// </summary>
    [Activity]
    public async Task EnsureAndEarnPointsAsync(EnsureLoyaltyInput input)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var client = ActivityExecutionContext.Current.TemporalClient;

        logger.LogInformation(
            "Ensuring loyalty account and earning {Points} points for user {UserId}, session {StripeSessionId}",
            input.Points, input.UserId, input.StripeSessionId);

        // Lazy-start the loyalty workflow — UseExisting makes this idempotent on retry.
        // If the workflow is already running, StartWorkflowAsync is a no-op.
        await client.StartWorkflowAsync(
            (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
            new WorkflowOptions($"loyalty-{input.UserId}", WorkflowConstants.DefaultTaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting
            });

        // Signal to earn points — idempotent by StripeSessionId inside the workflow.
        var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{input.UserId}");
        await handle.SignalAsync(wf => wf.EarnPointsAsync(
            new EarnPointsInput(input.StripeSessionId, input.ConfirmationNumber, input.Points)));

        logger.LogInformation(
            "EarnPoints signal sent: {Points} pts for user {UserId}, session {StripeSessionId}",
            input.Points, input.UserId, input.StripeSessionId);
    }

    /// <summary>
    /// Commits a pending points redemption after Stripe payment confirmation.
    /// Idempotent — the workflow's CommitRedemptionAsync signal is a no-op if already committed.
    /// Called from StripeCheckoutOrderWorkflow Step 6 using userId from Stripe session metadata,
    /// not from the StripeCustomerId lookup, so commit succeeds even if lookup fails.
    /// </summary>
    [Activity]
    public async Task CommitRedemptionAsync(string userId, string redemptionId)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var client = ActivityExecutionContext.Current.TemporalClient;

        logger.LogInformation(
            "Committing loyalty redemption {RedemptionId} for user {UserId}",
            redemptionId, userId);

        var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{userId}");
        await handle.SignalAsync(wf => wf.CommitRedemptionAsync(redemptionId));

        logger.LogInformation(
            "CommitRedemption signal sent: redemptionId {RedemptionId}, user {UserId}",
            redemptionId, userId);
    }
}
