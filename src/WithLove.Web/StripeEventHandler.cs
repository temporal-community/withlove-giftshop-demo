using Stripe;
using Stripe.Extensions.AspNetCore;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Exceptions;
using WithLove.Workflows;
using WithLove.Workflows.Workflows;

namespace WithLove.Web;

public class StripeEventHandler(ITemporalClient temporalClient, StripeWebhookContext context)
    : StripeWebhookHandler<StripeEventHandler>(context)
{
    private static readonly SearchAttributeKey<string> StripeSessionIdKey =
        SearchAttributeKey.CreateKeyword("StripeSessionId");

    private static readonly SearchAttributeKey<string> CustomerIdKey =
        SearchAttributeKey.CreateKeyword("CustomerId");

    public override async Task OnCheckoutSessionCompletedAsync(Event e)
    {
        var checkoutSession = (e.Data.Object as Stripe.Checkout.Session)!;
        var workflowId = $"process-order-{checkoutSession.Id}";
        var metadata = checkoutSession.Metadata;
        var input = new CheckoutOrderInput(
            checkoutSession.Id,
            checkoutSession.CustomerId,
            metadata?.GetValueOrDefault("redemptionId"),
            metadata?.GetValueOrDefault("userId"));

        try
        {
            Logger.LogInformation(
                "Starting order processing workflow for checkout session {SessionId}",
                checkoutSession.Id);

            // Fire-and-forget: start workflow without waiting for completion
            await temporalClient.StartWorkflowAsync(
                (StripeCheckoutOrderWorkflow wf) => wf.RunAsync(input),
                new WorkflowOptions(workflowId, WorkflowConstants.DefaultTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.AllowDuplicate,
                    ExecutionTimeout = TimeSpan.FromHours(24),
                    TypedSearchAttributes = new SearchAttributeCollection.Builder()
                        .Set(StripeSessionIdKey, checkoutSession.Id)
                        .Set(CustomerIdKey, checkoutSession.CustomerId ?? string.Empty)
                        .ToSearchAttributeCollection()
                });

            Logger.LogInformation(
                "Order processing workflow started for session {SessionId} with WorkflowId {WorkflowId}",
                checkoutSession.Id,
                workflowId);
        }
        catch (WorkflowAlreadyStartedException ex)
        {
            Logger.LogWarning(
                "Order processing workflow already started for session {SessionId}. RunId: {RunId}",
                checkoutSession.Id,
                ex.RunId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Failed to start order processing workflow for session {SessionId}",
                checkoutSession.Id);
            throw;
        }
    }
}