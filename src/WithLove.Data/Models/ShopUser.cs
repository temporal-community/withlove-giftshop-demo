using Microsoft.AspNetCore.Identity;

namespace WithLove.Data.Models;

public class ShopUser: IdentityUser
{
        public string StripeCustomerId { get; set; } = string.Empty;
}