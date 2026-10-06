using System.Security.Claims;
using EbayGroup5.Application.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

[Authorize(Roles = "Buyer,Seller"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MessagesController(IChatService chat) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? conversationId, CancellationToken ct)
    {
        if (!ModelState.IsValid || conversationId is <= 0) return BadRequest();
        var actorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (conversationId is int id)
        {
            try { await chat.ConversationAsync(actorId, id, ct); }
            catch (ChatException exception) when (exception.Error == ChatError.NotFound) { return NotFound(); }
        }
        ViewData["ActorId"] = actorId;
        ViewData["ConversationId"] = conversationId;
        return View();
    }
}
