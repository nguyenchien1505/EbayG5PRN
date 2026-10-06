using System.Security.Claims;
using EbayGroup5.Application.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// eBay-style "Report" for product reviews and seller feedback (D05). Reporters stay anonymous to everyone but admins.
[Authorize(Roles = "Buyer,Seller")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(IModerationService moderation) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    internal static ContentKind? Kind(string? kind) => kind switch { "review" => ContentKind.Review, "feedback" => ContentKind.SellerFeedback, _ => null };

    [HttpGet]
    public async Task<IActionResult> Create(string? kind, int id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || Kind(kind) is not { } k || id < 1) return BadRequest();
        var context = await moderation.FindReportableAsync(k, id, cancellationToken);
        return context == null ? NotFound() : View(context);
    }

    [HttpPost]
    public async Task<IActionResult> Create(string? kind, int id, string? reason, string? details, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || Kind(kind) is not { } k || id < 1) return BadRequest();
        ReportReason? parsed = Enum.TryParse<ReportReason>(reason, ignoreCase: false, out var r) && Enum.IsDefined(r) && !int.TryParse(reason, out _) ? r : null;
        var result = parsed == null ? new ModerationResult(ModerationOutcome.Invalid, "Choose a reason for your report.")
            : await moderation.ReportAsync(ActorId, k, id, parsed.Value, details, cancellationToken);
        if (result.Outcome == ModerationOutcome.Success)
        {
            TempData["SiteNotice"] = "Thanks — we'll review your report.";
            return LocalRedirect(result.ReturnPath!);
        }
        if (result.Outcome == ModerationOutcome.NotFound) return NotFound();
        var context = await moderation.FindReportableAsync(k, id, cancellationToken);
        if (context == null) return NotFound();
        Response.StatusCode = result.Outcome switch { ModerationOutcome.Forbidden => 403, ModerationOutcome.Conflict => 409, _ => 400 };
        ViewData["ReportError"] = result.Error ?? "You can't report this content.";
        ViewData["ReportDetails"] = details;
        return View(context);
    }
}
