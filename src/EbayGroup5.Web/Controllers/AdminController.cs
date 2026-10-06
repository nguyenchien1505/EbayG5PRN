using System.Security.Claims;
using EbayGroup5.Application.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// Admin moderation (D05). AdminDemo policy plus the AdminNetwork middleware; the service rechecks the database role.
[Authorize(Policy = "AdminDemo")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IModerationService moderation) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Moderation(CancellationToken cancellationToken)
    {
        var queue = await moderation.QueueAsync(ActorId, cancellationToken);
        return queue == null ? StatusCode(StatusCodes.Status403Forbidden) : View(queue);
    }

    [HttpGet]
    public async Task<IActionResult> ModerationItem(string? kind, int id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || ReportsController.Kind(kind) is not { } k || id < 1) return BadRequest();
        var detail = await moderation.DetailAsync(ActorId, k, id, cancellationToken);
        return detail == null ? NotFound() : View(detail);
    }

    [HttpPost]
    public async Task<IActionResult> Moderate(string? kind, int id, string? action, string? note, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || ReportsController.Kind(kind) is not { } k || id < 1) return BadRequest();
        ModerationAction? parsed = Enum.TryParse<ModerationAction>(action, ignoreCase: false, out var a) && Enum.IsDefined(a) && !int.TryParse(action, out _) ? a : null;
        var result = parsed == null ? new ModerationResult(ModerationOutcome.Invalid, "Choose a moderation action.")
            : await moderation.ActAsync(ActorId, k, id, parsed.Value, note, cancellationToken);
        switch (result.Outcome)
        {
            case ModerationOutcome.Success:
                TempData["SiteNotice"] = $"{parsed} recorded in the moderation audit.";
                return RedirectToAction(nameof(ModerationItem), new { kind, id });
            case ModerationOutcome.NotFound: return NotFound();
            case ModerationOutcome.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
        }
        var detail = await moderation.DetailAsync(ActorId, k, id, cancellationToken);
        if (detail == null) return NotFound();
        Response.StatusCode = result.Outcome == ModerationOutcome.Conflict ? 409 : 400;
        ViewData["ModerationError"] = result.Error;
        return View(nameof(ModerationItem), detail);
    }
}
