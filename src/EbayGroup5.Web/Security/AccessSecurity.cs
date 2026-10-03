using System.Net;
using System.Security.Claims;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Web.Security;

public sealed class AccountOwnerRequirement : IAuthorizationRequirement;

public sealed class AccountOwnerHandler : AuthorizationHandler<AccountOwnerRequirement, User>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AccountOwnerRequirement requirement, User resource)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id == resource.Id)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class AccessCookieEvents(EbayDbContext db) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var user = int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted) : null;
        if (user == null || string.IsNullOrEmpty(user.Password) || user.Role is not ("Buyer" or "Seller" or "Admin") ||
            user.Role != principal?.FindFirstValue(ClaimTypes.Role))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

public sealed class AdminNetwork
{
    private readonly HashSet<IPAddress> addresses;

    public AdminNetwork(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Security:AdminAllowedAddresses").Get<string[]>() ?? ["127.0.0.1", "::1"];
        addresses = configured.Select(value => IPAddress.Parse(value).MapToIPv6()).ToHashSet();
    }

    public bool Allows(IPAddress? address) => address != null && addresses.Contains(address.MapToIPv6());
}
