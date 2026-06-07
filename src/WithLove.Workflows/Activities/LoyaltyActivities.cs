using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using WithLove.Data;
using WithLove.Workflows.Loyalty;
using WithLove.Workflows.Workflows;

namespace WithLove.Workflows.Activities;

/// <summary>Love Tokens activities. Use ActivityExecutionContext.Current.TemporalClient inside methods.</summary>
public class LoyaltyActivities(ProductsDbContext dbContext)
{
    /// <summary>Returns null when the Stripe customer is not linked to an app user.</summary>
    [Activity]
    public async Task<string?> ResolveUserIdByStripeCustomerIdAsync(string stripeCustomerId)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.ResolvingUserByStripeCustomer(stripeCustomerId);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId);

        if (user is null)
        {
            logger.NoUserForStripeCustomer(stripeCustomerId);
            return null;
        }

        logger.ResolvedUserByStripeCustomer(stripeCustomerId, user.Id);

        return user.Id;
    }

    /// <summary>Starts the workflow if needed and sends an idempotent earn signal.</summary>
    [Activity]
    public async Task EnsureAndEarnPointsAsync(EnsureLoyaltyInput input)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var client = ActivityExecutionContext.Current.TemporalClient;

        logger.EnsuringLoyaltyEarn(input.Points, input.UserId, input.StripeSessionId);

        await client.StartWorkflowAsync(
            (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
            new WorkflowOptions($"loyalty-{input.UserId}", WorkflowConstants.DefaultTaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting
            });

        var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{input.UserId}");
        await handle.SignalAsync(wf => wf.EarnPointsAsync(
            new EarnPointsInput(input.StripeSessionId, input.ConfirmationNumber, input.Points)));

        logger.EarnPointsSignalSent(input.Points, input.UserId, input.StripeSessionId);
    }

    /// <summary>Commits by metadata userId, independent of Stripe customer lookup.</summary>
    [Activity]
    public async Task CommitRedemptionAsync(string userId, string redemptionId)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var client = ActivityExecutionContext.Current.TemporalClient;

        logger.CommittingRedemption(redemptionId, userId);

        var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{userId}");
        await handle.SignalAsync(wf => wf.CommitRedemptionAsync(redemptionId));

        logger.CommitRedemptionSignalSent(redemptionId, userId);
    }
}
