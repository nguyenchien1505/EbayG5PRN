using System.Security.Claims;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Application.Moderation;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// Admin moderation (D05) and request reviews (E05). AdminDemo policy plus the AdminNetwork middleware; services recheck the database role and credential.
[Authorize(Policy = "AdminDemo")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IModerationService moderation, IDisputeService disputes, DisputeTiming timing, TimeProvider clock) : Controller
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

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet]
    public async Task<IActionResult> Disputes(DisputeQueueState state = DisputeQueueState.Waiting, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(state) || page < 1 || page > int.MaxValue / DisputeLimits.QueuePageSize) return BadRequest();
        var queue = await disputes.QueueAsync(ActorId, new(state, page), cancellationToken);
        return queue == null ? StatusCode(StatusCodes.Status403Forbidden) : View(new AdminDisputeQueuePage(queue, timing, Now));
    }

    [HttpGet]
    public async Task<IActionResult> Dispute(int id, int activity = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id < 1 || activity < 1 || activity > int.MaxValue / DisputeLimits.TimelinePageSize) return BadRequest();
        var detail = await disputes.FindAsync(ActorId, id, activity, cancellationToken);
        return detail is not { Viewer: DisputeActor.Admin } ? NotFound() : View(new AdminDisputePage(detail, null, timing, Now));
    }

    [HttpPost]
    public async Task<IActionResult> DisputeAct([FromForm] AdminDisputeForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || form.DisputeId < 1 || !Enum.IsDefined(form.Action) || !AdminDisputeForm.Postable(form.Action)) return BadRequest();
        var decide = form.Action == DisputeAction.Decide;
        var result = decide && form.ForBuyer == null
            ? new DisputeResult(DisputeResultCode.Invalid, Error: "Choose who the decision favors.")
            : await disputes.ActAsync(ActorId, form.DisputeId, new DisputeCommand(form.Action, form.Message, ForBuyer: decide ? form.ForBuyer : null), cancellationToken);
        switch (result.Code)
        {
            case DisputeResultCode.Success:
                TempData["SiteNotice"] = decide ? "Decision recorded. The request is closed." : "Message sent to the buyer and seller.";
                return RedirectToAction(nameof(Dispute), new { id = form.DisputeId });
            case DisputeResultCode.NotFound: return NotFound();
            case DisputeResultCode.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
        }
        var detail = await disputes.FindAsync(ActorId, form.DisputeId, cancellationToken);
        if (detail is not { Viewer: DisputeActor.Admin }) return NotFound();
        Response.StatusCode = result.Code == DisputeResultCode.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        ViewData["DisputeError"] = result.Error;
        // Invalid input keeps what the admin typed; a conflict shows the current state.
        return View(nameof(Dispute), new AdminDisputePage(detail, result.Code == DisputeResultCode.Invalid ? form : null, timing, Now));
    }
}
