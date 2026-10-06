using System.Globalization;
using System.Security.Claims;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// E02 buyer forms: "I didn't receive it" and "Return this item" with server drafts (DISPUTE_FORMS.md).
// E03 request list, details/timeline and replies for the request's buyer and seller (DISPUTE_DETAILS.md).
[Authorize(Roles = "Buyer,Seller")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DisputesController(IDisputeService disputes, DisputeTiming timing, IDisputeAutomation automation, ILogger<DisputesController> logger) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // E04: write the automatic notice right away; the state is already committed, so a failure only defers it to the background job.
    private async Task Notify(int disputeId)
    {
        try { await automation.NotifyAsync(disputeId, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Automatic notice for request {DisputeId} deferred to the background job.", disputeId); }
    }

    [HttpGet, Authorize(Policy = "Buyer")]
    public Task<IActionResult> NotReceived(int orderItemId, CancellationToken cancellationToken) => Form(orderItemId, DisputeKind.ItemNotReceived, cancellationToken);

    [HttpGet, Authorize(Policy = "Buyer")]
    public Task<IActionResult> Return(int orderItemId, CancellationToken cancellationToken) => Form(orderItemId, DisputeKind.Return, cancellationToken);

    private async Task<IActionResult> Form(int orderItemId, DisputeKind kind, CancellationToken ct)
    {
        if (!ModelState.IsValid || orderItemId < 1) return BadRequest();
        var line = await disputes.EligibilityAsync(ActorId, orderItemId, ct);
        if (line == null) return NotFound();
        if (!line.For(kind).CanOpen) return Unavailable(line, kind, line.For(kind).Reason);
        var draft = await disputes.FindDraftAsync(ActorId, orderItemId, kind, ct);
        var form = new DisputeForm
        {
            OrderItemId = orderItemId, Kind = kind, Reason = kind == DisputeKind.ItemNotReceived ? DisputeReason.ItemNotReceived : draft?.Reason,
            Preference = draft?.Preference, Details = draft?.Details
        };
        return View(ViewName(kind), new DisputeFormPage(line, kind, form, draft != null, timing));
    }

    [HttpPost, Authorize(Policy = "Buyer")]
    public async Task<IActionResult> Open([FromForm] DisputeForm form, CancellationToken cancellationToken)
    {
        if (ModelState.TryGetValue(nameof(DisputeForm.Kind), out var kindState) && kindState.Errors.Count > 0 || !Enum.IsDefined(form.Kind)) return BadRequest();
        if (!ModelState.IsValid) return await Redisplay(form, StatusCodes.Status400BadRequest, "Check your answers and try again.", cancellationToken);
        var inr = form.Kind == DisputeKind.ItemNotReceived;
        if (!inr && form.Reason == null) return await Redisplay(form, StatusCodes.Status400BadRequest, "Choose why you're returning this item.", cancellationToken);
        var reason = inr ? DisputeReason.ItemNotReceived : form.Reason!.Value;
        var result = await disputes.OpenAsync(ActorId, new(form.OrderItemId, form.Kind, reason, inr ? form.Preference : null, form.Details), cancellationToken);
        switch (result.Code)
        {
            case DisputeResultCode.Success:
                await Notify(result.DisputeId!.Value);
                var opened = await disputes.FindAsync(ActorId, result.DisputeId!.Value, cancellationToken);
                TempData["OrderNotice"] = opened?.DueAtUtc is { } due
                    ? $"Your request was sent. The seller has until {DisputeText.Date(due, timing)} to respond." : "Your request was sent.";
                return RedirectToLine(opened!.OrderId, form.OrderItemId);
            case DisputeResultCode.Conflict when result.ExistingDisputeId is int existing
                && await disputes.FindAsync(ActorId, existing, cancellationToken) is { Viewer: DisputeActor.Buyer, Status: not DisputeStatus.Closed } same && same.Kind == form.Kind:
                // Resubmission (double click, back + resend): the request already exists, so nothing new is created.
                TempData["OrderNotice"] = "This request was already sent.";
                return RedirectToLine(same.OrderId, form.OrderItemId);
            case DisputeResultCode.NotFound: return NotFound();
            case DisputeResultCode.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
            case DisputeResultCode.Invalid: return await Redisplay(form, StatusCodes.Status400BadRequest, result.Error, cancellationToken);
            default: return await Redisplay(form, StatusCodes.Status409Conflict, result.Error, cancellationToken);
        }
    }

    [HttpPost, Authorize(Policy = "Buyer")]
    public async Task<IActionResult> Draft([FromForm] DisputeForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(form.Kind)) return BadRequest(new { error = "Invalid draft." });
        var reason = form.Kind == DisputeKind.ItemNotReceived ? null : form.Reason;
        var result = await disputes.SaveDraftAsync(ActorId, new(form.OrderItemId, form.Kind, reason, form.Kind == DisputeKind.ItemNotReceived ? form.Preference : null, form.Details), cancellationToken);
        return result.Code switch
        {
            DisputeResultCode.Success => Ok(new { savedAtUtc = result.SavedAtUtc }),
            DisputeResultCode.Invalid => BadRequest(new { error = result.Error }),
            DisputeResultCode.NotFound => NotFound(),
            DisputeResultCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            _ => Conflict(new { error = result.Error })
        };
    }

    private static string ViewName(DisputeKind kind) => kind == DisputeKind.ItemNotReceived ? "NotReceived" : "Return";

    private IActionResult RedirectToLine(int orderId, int orderItemId) => Redirect(Url.Action("Details", "Orders", new { id = orderId }) + "#line-" + orderItemId);

    private IActionResult RedirectToRequest(int disputeId) => RedirectToAction(nameof(Details), new { id = disputeId });

    [HttpGet]
    public async Task<IActionResult> Index(DisputeKind? kind, DisputeListState? state, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || page < 1 || page > int.MaxValue / DisputeLimits.ListPageSize || kind is { } k && !Enum.IsDefined(k) || state is { } st && !Enum.IsDefined(st))
            return BadRequest();
        var list = await disputes.ListAsync(ActorId, new(kind, state, page), cancellationToken);
        return list == null ? StatusCode(StatusCodes.Status403Forbidden) : View("Index", new DisputeListPage(list, timing));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, int activity = 1, DisputeAction? reply = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id < 1 || activity < 1 || activity > int.MaxValue / DisputeLimits.TimelinePageSize || reply is { } r && !DisputeRequestText.Postable(r))
            return BadRequest();
        var detail = await disputes.FindAsync(ActorId, id, activity, cancellationToken);
        if (detail == null) return NotFound();
        if (reply is { } chosen && !detail.Actions.Contains(chosen))
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            ViewData["DisputeError"] = "That option isn't available for this request right now.";
            reply = null;
        }
        return View("Details", new DisputeDetailsPage(detail, reply, reply == null ? null : new DisputeActForm { DisputeId = id, Action = reply.Value }, timing));
    }

    [HttpPost]
    public async Task<IActionResult> Act([FromForm] DisputeActForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || form.DisputeId < 1 || !Enum.IsDefined(form.Action) || !DisputeRequestText.Postable(form.Action)) return BadRequest();
        string? message = form.Message;
        if (DisputeRequestText.ReasonsFor(form.Action) is { } reasons)
        {
            var label = reasons.FirstOrDefault(x => x.Code == form.Reason).Label;
            if (label == null) return await RedisplayRequest(form, StatusCodes.Status400BadRequest, "Choose a reason.", cancellationToken);
            if (form.Message?.Trim().Length > DisputeRequestText.MaxReasonNote)
                return await RedisplayRequest(form, StatusCodes.Status400BadRequest, $"Notes must be at most {DisputeRequestText.MaxReasonNote} characters.", cancellationToken);
            message = DisputeRequestText.Compose(label, form.Message);
        }
        decimal? amount = null;
        if (form.Action == DisputeAction.OfferPartial)
        {
            if (!decimal.TryParse(form.Amount?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
                return await RedisplayRequest(form, StatusCodes.Status400BadRequest, "Enter an amount such as 12.50.", cancellationToken);
            amount = parsed;
        }
        var tracking = form.Action is DisputeAction.AddTracking or DisputeAction.MarkReturnShipped ? form.Tracking : null;
        var result = await disputes.ActAsync(ActorId, form.DisputeId, new DisputeCommand(form.Action, message, amount, tracking), cancellationToken);
        switch (result.Code)
        {
            case DisputeResultCode.Success:
                await Notify(form.DisputeId);
                TempData["DisputeNotice"] = DisputeRequestText.Notice(form.Action);
                return RedirectToRequest(form.DisputeId);
            case DisputeResultCode.NotFound: return NotFound();
            case DisputeResultCode.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
            case DisputeResultCode.Invalid: return await RedisplayRequest(form, StatusCodes.Status400BadRequest, result.Error, cancellationToken);
            default: return await RedisplayRequest(form, StatusCodes.Status409Conflict, result.Error, cancellationToken);
        }
    }

    // Invalid input keeps the chosen reply and what was typed; a conflict goes back to step 1 with the current state.
    private async Task<IActionResult> RedisplayRequest(DisputeActForm form, int status, string? error, CancellationToken ct)
    {
        var detail = await disputes.FindAsync(ActorId, form.DisputeId, ct);
        if (detail == null) return NotFound();
        Response.StatusCode = status;
        ViewData["DisputeError"] = error;
        var keep = status == StatusCodes.Status400BadRequest && detail.Actions.Contains(form.Action);
        return View("Details", new DisputeDetailsPage(detail, keep ? form.Action : null, keep ? form : null, timing));
    }

    private IActionResult Unavailable(DisputeEligibility line, DisputeKind kind, string? reason)
    {
        Response.StatusCode = StatusCodes.Status409Conflict;
        return View("Unavailable", new DisputeUnavailablePage(line, kind, reason, timing));
    }

    // Re-render only for an order line the actor owns, keeping what the buyer typed; otherwise return the bare status.
    private async Task<IActionResult> Redisplay(DisputeForm form, int status, string? error, CancellationToken ct)
    {
        var line = form.OrderItemId > 0 && Enum.IsDefined(form.Kind) ? await disputes.EligibilityAsync(ActorId, form.OrderItemId, ct) : null;
        if (line == null) return StatusCode(status);
        if (!line.For(form.Kind).CanOpen) return Unavailable(line, form.Kind, error ?? line.For(form.Kind).Reason);
        Response.StatusCode = status;
        ViewData["DisputeError"] = error;
        if (form.Kind == DisputeKind.ItemNotReceived) form.Reason = DisputeReason.ItemNotReceived;
        return View(ViewName(form.Kind), new DisputeFormPage(line, form.Kind, form, false, timing));
    }
}
