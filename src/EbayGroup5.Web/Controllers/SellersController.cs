using System.Security.Claims;
using EbayGroup5.Application.Feedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// F1 simplified seller feedback profile (public) and one-time seller replies (D04).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SellersController(IFeedbackService feedback) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Feedback(int id, string? sentiment = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var known = sentiment is null or "positive" or "neutral" or "negative";
        FeedbackSentiment? filter = sentiment switch { "positive" => FeedbackSentiment.Positive, "neutral" => FeedbackSentiment.Neutral, "negative" => FeedbackSentiment.Negative, _ => null };
        if (!ModelState.IsValid || !known || page <= 0 || page > int.MaxValue / SellerFeedbackPage.PageSize)
        {
            Response.StatusCode = 400;
            return View("InvalidFeedbackFilters", id);
        }
        var profile = await feedback.ProfileAsync(id, filter, page, cancellationToken);
        return profile == null ? NotFound() : View(profile);
    }

    [HttpPost, Authorize(Policy = "Seller")]
    public async Task<IActionResult> Reply(int feedbackId, string? reply, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || feedbackId < 1) return BadRequest();
        var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await feedback.ReplyAsync(actor, feedbackId, reply, cancellationToken);
        switch (result.Outcome)
        {
            case ReplyOutcome.Success:
                TempData["FeedbackNotice"] = "Your reply was posted.";
                return RedirectToAction(nameof(Feedback), new { id = actor });
            case ReplyOutcome.NotFound: return NotFound();
            case ReplyOutcome.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
        }
        var profile = await feedback.ProfileAsync(actor, cancellationToken: cancellationToken);
        if (profile == null) return StatusCode(StatusCodes.Status403Forbidden);
        Response.StatusCode = result.Outcome == ReplyOutcome.Invalid ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict;
        ViewData["ReplyError"] = result.Error;
        ViewData["ReplyDraftId"] = feedbackId;
        ViewData["ReplyDraft"] = reply;
        return View(nameof(Feedback), profile);
    }
}
