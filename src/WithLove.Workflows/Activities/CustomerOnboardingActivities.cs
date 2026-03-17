using Microsoft.EntityFrameworkCore;
using Stripe;
using Temporalio.Activities;
using Temporalio.Exceptions;
using WithLove.Data;

namespace WithLove.Workflows.Activities;

public class CustomerOnboardingActivities(ProductsDbContext dbContext, StripeClient stripeClient)
{
    [Activity]
    public async Task<CreateStripeCustomerResponse> CreateStripeCustomerAsync(CreateStripeCustomerInput input)
    {
        var ccOptions = new CustomerCreateOptions {
            Name = input.CustomerName,
            Email = input.CustomerEmail,
            Description = $"Demo User Account {input.CustomerEmail}",
            PaymentMethod = "pm_card_visa",

            InvoiceSettings =
                new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = "pm_card_visa" }
        };
        
        var stripeCustomer = await stripeClient.V1.Customers.CreateAsync(ccOptions, cancellationToken: ActivityExecutionContext.Current.CancellationToken);
        
        return new CreateStripeCustomerResponse(stripeCustomer.Id);
    }

    [Activity]
    public async Task UpdateCustomerStripeIdAsync(UpdateCustomerStripeIdInput input)
    {
        var shopUser = await dbContext.Users.Where(s => s.Email == input.CustomerEmail).FirstOrDefaultAsync();
        
        if (shopUser == null)
            throw new ApplicationFailureException($"ShopUser not found for email {input.CustomerEmail}", nonRetryable: true);

        shopUser.StripeCustomerId = input.StripeCustomerId;
        dbContext.Update(shopUser);
       var changes = await dbContext.SaveChangesAsync();
        
        if (changes <=0)
        {
            throw new ApplicationFailureException(
                $"Failed to update StripeCustomerId for {input.CustomerEmail}");
        }
    }
}

public record CreateStripeCustomerInput(string CustomerName, string CustomerEmail);
public record CreateStripeCustomerResponse(string StripeCustomerId);

public record UpdateCustomerStripeIdInput(string CustomerEmail, string StripeCustomerId);