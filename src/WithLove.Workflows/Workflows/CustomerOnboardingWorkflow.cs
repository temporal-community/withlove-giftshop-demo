using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using WithLove.Workflows.Activities;

namespace WithLove.Workflows.Workflows;

[Workflow]
public class CustomerOnboardingWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(CustomerOnboardingInput input)
    {
        Workflow.Logger.LogInformation("Starting onboarding workflow for customer {CustomerId}", input.CustomerEmail);

        var stripeCustomerResult= await Workflow.ExecuteActivityAsync((CustomerOnboardingActivities ac) => 
                ac.CreateStripeCustomerAsync(new(input.CustomerName, input.CustomerEmail )),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new()
                {
                    InitialInterval = TimeSpan.FromSeconds(10),
                    BackoffCoefficient = 2.0f,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 3
                }
            });
         
        await Workflow.ExecuteActivityAsync((CustomerOnboardingActivities ac) =>
            ac.UpdateCustomerStripeIdAsync(new(input.CustomerEmail, stripeCustomerResult.StripeCustomerId)),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new()
                {
                    InitialInterval = TimeSpan.FromSeconds(10),
                    BackoffCoefficient = 2.0f,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 3
                }
            });
      
        Workflow.Logger.LogInformation("Completed onboarding workflow for customer {CustomerId}", input.CustomerEmail);
    }
    
}

public record CustomerOnboardingInput(string CustomerName, string CustomerEmail); 