using Microsoft.AspNetCore.Identity;

namespace WithLove.Data.Models;

public class ShopUser: IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string StripeCustomerId { get; set; } = string.Empty;
}