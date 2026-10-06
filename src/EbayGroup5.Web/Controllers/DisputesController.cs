using System.Security.Claims;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// E02 buyer forms: "I didn't receive it" and "Return this item" with server drafts (DISPUTE_FORMS.md).
[Authorize(Policy = "Buyer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DisputesController(IDisputeService disputes, DisputeTiming timing) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public Task<IActionResult> NotReceived(int orderItemId, CancellationToken cancellationToken) => Form(orderItemId, DisputeKind.ItemNotReceived, cancellationToken);

    [HttpGet]
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

    [HttpPost]
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

    [HttpPost]
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
