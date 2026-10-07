using System.Security.Claims;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Web.Chat;

public sealed class ChatHubFilter(EbayDbContext db, SharedRateLimiter limiter, SessionRevocation revocation, ILogger<ChatHubFilter> logger) : IHubFilter
{
    private async Task Validate(HubCallerContext context)
    {
        var principal = context.User;
        var expires = context.GetHttpContext()?.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties?.ExpiresUtc;
        var role = principal?.FindFirstValue(ClaimTypes.Role);
        var actor = int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.ConnectionAborted) : null;
        if (principal?.Identity?.IsAuthenticated != true || role is not ("Buyer" or "Seller") ||
            actor?.Role != role || string.IsNullOrEmpty(actor.Password) || expires == null || expires <= DateTimeOffset.UtcNow ||
            SessionRevocation.SessionId(principal) is not { } sid || await revocation.IsRevokedAsync(sid))
        {
            context.Abort();
            throw new HubException("401: Sign in is required.");
        }
    }
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await Validate(context.Context);
        // F05 (LOAD.md): which instance holds whose socket (ids only, never content) — lets the load test prove cross-instance chat.
        logger.LogInformation("Chat connection opened user={UserId}", context.Context.User!.FindFirstValue(ClaimTypes.NameIdentifier));
        await next(context);
    }
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await Validate(context.Context);
        // F02: every invocation counts against the caller's shared budget (all instances); the connection stays open.
        var user = context.Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!(await limiter.AcquireAsync(SharedRateLimiter.ChatHub, "u:" + user)).Allowed)
            throw new HubException("429: You're doing that too often. Wait a moment and try again.");
        try { return await next(context); }
        catch (ChatException exception)
        {
            var code = exception.Error switch { ChatError.Invalid => 400, ChatError.Forbidden => 403, ChatError.NotFound => 404, _ => 409 };
            throw new HubException($"{code}: {exception.Message}");
        }
        catch (OperationCanceledException) when (context.Context.ConnectionAborted.IsCancellationRequested) { throw; }
        catch
        {
            logger.LogWarning("Chat invocation failed method={Method} result=500", context.HubMethodName);
            throw new HubException("500: An unexpected error occurred.");
        }
    }
}
