using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using WithLove.Data.Models;

namespace WithLove.Web.Services;

/// <summary>
/// Extends the default claims principal with ShopUser-specific claims.
/// Adds FullName as ClaimTypes.Name so it's available throughout the app
/// (profile page, chat agent context, etc.) without additional lookups.
/// </summary>
public class ShopUserClaimsPrincipalFactory(
    UserManager<ShopUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ShopUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ShopUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrEmpty(user.FullName))
        {
            // Replace the default UserName-based Name claim with the actual full name
            var existing = identity.FindFirst(ClaimTypes.Name);
            if (existing is not null)
                identity.RemoveClaim(existing);

            identity.AddClaim(new Claim(ClaimTypes.Name, user.FullName));
        }

        return identity;
    }
}
