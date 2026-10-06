using System.Security.Claims;
using EbayGroup5.Application.Chat;
using EbayGroup5.Web.Chat;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

[ApiController, Authorize, Route("api/v1/chat"), ServiceFilter(typeof(ChatApiFilter))]
public sealed class ChatApiController(IChatService chat, ChatRealtime realtime, IAntiforgery antiforgery) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record OpenRequest(int? ProductId, int? OrderItemId);
    public sealed record ReadRequest(string ThroughSequence);

    [HttpGet("antiforgery")]
    public IActionResult Token() => Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("conversations"), Authorize(Policy = "Buyer"), RequestSizeLimit(ChatLimits.MaxBodyBytes)]
    public async Task<IActionResult> Open(OpenRequest request, CancellationToken ct)
    {
        if ((request.ProductId == null) == (request.OrderItemId == null)) throw new ChatException(ChatError.Invalid);
        return Ok(request.ProductId is int product
            ? await chat.OpenForProductAsync(ActorId, product, ct)
            : await chat.OpenForOrderItemAsync(ActorId, request.OrderItemId!.Value, ct));
    }

    [HttpGet("conversations"), Authorize(Roles = "Buyer,Seller")]
    public async Task<IActionResult> Inbox(DateTimeOffset? cursorUtc, int? cursorId, int limit = ChatLimits.DefaultInboxSize, CancellationToken ct = default)
    {
        if ((cursorUtc == null) != (cursorId == null)) throw new ChatException(ChatError.Invalid);
        return Ok(await chat.InboxAsync(ActorId, cursorUtc != null ? new(cursorUtc.Value, cursorId!.Value) : null, limit, ct));
    }

    [HttpGet("conversations/{id:int}/messages")]
    public async Task<IActionResult> History(int id, string? before, string? after, int limit = ChatLimits.DefaultHistorySize, CancellationToken ct = default) =>
        Ok(await chat.HistoryAsync(ActorId, id, new(before, after, limit), ct));

    [HttpPost("conversations/{id:int}/messages"), RequestSizeLimit(ChatLimits.MaxBodyBytes)]
    public async Task<IActionResult> Send(int id, ChatSendCommand command, CancellationToken ct) => Ok(await realtime.SendAsync(ActorId, id, command, ct));

    [HttpPost("conversations/{id:int}/read"), RequestSizeLimit(ChatLimits.MaxBodyBytes)]
    public async Task<IActionResult> Read(int id, ReadRequest request, CancellationToken ct) => Ok(await realtime.ReadAsync(ActorId, id, request.ThroughSequence, ct));
}
