using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
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
        var meter = ActivityExecutionContext.Current.MetricMeter;

        logger.EnsuringLoyaltyEarn(input.Points, input.UserId, input.StripeSessionId);

        // Attempt to start a new workflow; if it already exists, use the existing one.
        // This lets us detect account creation (new workflow) vs. an existing account.
        var isNewAccount = false;
        try
        {
            await client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions($"loyalty-{input.UserId}", WorkflowConstants.DefaultTaskQueue)
                {
                    IdConflictPolicy = WorkflowIdConflictPolicy.Fail
                });
            isNewAccount = true;
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Workflow is already running — this is an existing account.
        }

        if (isNewAccount)
        {
            meter.CreateCounter<long>("loyalty.account.created").Add(1);
        }

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
        var meter = ActivityExecutionContext.Current.MetricMeter;

        logger.CommittingRedemption(redemptionId, userId);

        var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{userId}");
        await handle.SignalAsync(wf => wf.CommitRedemptionAsync(redemptionId));

        meter.CreateHistogram<long>("loyalty.redemption.committed", unit: "points").Record(1);

        logger.CommitRedemptionSignalSent(redemptionId, userId);
    }
}
