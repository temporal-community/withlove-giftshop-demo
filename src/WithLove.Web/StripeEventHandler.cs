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
            Logger.StartingOrderProcessingWorkflow(checkoutSession.Id);

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

            Logger.OrderProcessingWorkflowStarted(checkoutSession.Id, workflowId);
        }
        catch (WorkflowAlreadyStartedException ex)
        {
            Logger.OrderProcessingWorkflowAlreadyStarted(checkoutSession.Id, ex.RunId);
        }
        catch (Exception ex)
        {
            Logger.FailedToStartOrderProcessingWorkflow(ex, checkoutSession.Id);
            throw;
        }
    }
}
