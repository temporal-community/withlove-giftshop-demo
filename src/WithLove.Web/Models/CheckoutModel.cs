namespace WithLove.Web.Models;

public class CheckoutModel
{
    public string RecipientFirstName { get; set; } = "";

    public string Message { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Email is required")]
    [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "Please enter a valid email")]
    public string BillingEmail { get; set; } = "";
}
