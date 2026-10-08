using System.Security.Claims;
using EbayGroup5.Application.Feedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// F1 simplified seller feedback profile (public) and one-time seller replies (D04).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SellersController(IFeedbackService feedback) : Controller
{
    // F2 classic profile query string: tab, sentiment (rating links), period (months), q (item title or ID), sort, page, size.
    [HttpGet]
    public async Task<IActionResult> Feedback(int id, string? tab = null, string? sentiment = null, int? period = null, string? q = null, string? sort = null,
        int page = 1, int size = SellerFeedbackPage.PageSize, CancellationToken cancellationToken = default)
    {
        FeedbackTab? feedbackTab = tab switch { null or "all" => FeedbackTab.All, "buyer" => FeedbackTab.Buyer, "seller" => FeedbackTab.Seller, "left" => FeedbackTab.Left, _ => null };
        var knownSentiment = sentiment is null or "positive" or "neutral" or "negative";
        FeedbackSentiment? filter = sentiment switch { "positive" => FeedbackSentiment.Positive, "neutral" => FeedbackSentiment.Neutral, "negative" => FeedbackSentiment.Negative, _ => null };
        FeedbackPeriod? feedbackPeriod = period switch { null => FeedbackPeriod.All, 1 => FeedbackPeriod.PastMonth, 6 => FeedbackPeriod.Past6Months, 12 => FeedbackPeriod.PastYear, _ => null };
        FeedbackSort? feedbackSort = sort switch { null or "relevant" => FeedbackSort.Relevant, "recent" => FeedbackSort.Recent, _ => null };
        if (!ModelState.IsValid || feedbackTab == null || !knownSentiment || feedbackPeriod == null || feedbackSort == null
            || (q?.Trim().Length ?? 0) > FeedbackProfileQuery.MaxSearchLength || !FeedbackProfileQuery.PageSizes.Contains(size) || page <= 0 || page > int.MaxValue / size)
        {
            Response.StatusCode = 400;
            return View("InvalidFeedbackFilters", id);
        }
        var profile = await feedback.ProfileAsync(id, new FeedbackProfileQuery(feedbackTab.Value, filter, feedbackPeriod.Value, q, feedbackSort.Value, page, size), cancellationToken);
        if (profile == null) return NotFound();
        ViewData["DetailedRatingsMinimum"] = feedback.DetailedRatingsMinimum;
        return View(profile);
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
