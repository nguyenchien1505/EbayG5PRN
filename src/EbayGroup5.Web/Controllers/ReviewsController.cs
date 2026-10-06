using System.Security.Claims;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// Combined R1 "Leave feedback" page: seller experience (D03) and item review (D01/D02) for one order line.
[Authorize(Policy = "Buyer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewsController(IReviewService reviews, IFeedbackService feedback) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Create(int orderItemId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || orderItemId < 1) return BadRequest();
        var page = await Load(new ReviewForm { OrderItemId = orderItemId }, cancellationToken);
        return page == null ? NotFound() : View(page);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromForm] ReviewForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return await Redisplay(form, StatusCodes.Status400BadRequest,
                ModelState.ContainsKey(nameof(ReviewForm.Sentiment)) && ModelState[nameof(ReviewForm.Sentiment)]!.Errors.Count > 0
                    ? "Choose Positive, Neutral or Negative for the seller." : "Select a star rating from 1 to 5.", cancellationToken);
        var result = await feedback.SubmitAsync(ActorId, new(form.OrderItemId, form.Sentiment, form.SellerComment, form.Rating, form.Comment), cancellationToken);
        switch (result.Outcome)
        {
            case LeaveFeedbackOutcome.Success:
                TempData["OrderNotice"] = result.FeedbackPosted && result.ReviewPosted ? "Thanks — your feedback and review were posted."
                    : result.FeedbackPosted ? "Thanks — your feedback was posted." : "Thanks — your review was posted.";
                return RedirectToAction("Details", "Orders", new { id = result.OrderId });
            case LeaveFeedbackOutcome.NotFound: return NotFound();
            case LeaveFeedbackOutcome.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
            case LeaveFeedbackOutcome.Invalid: return await Redisplay(form, StatusCodes.Status400BadRequest, result.Error, cancellationToken);
            default: return await Redisplay(form, StatusCodes.Status409Conflict, result.Error, cancellationToken);
        }
    }

    private async Task<ReviewPage?> Load(ReviewForm form, CancellationToken ct)
    {
        var context = await reviews.FindItemAsync(ActorId, form.OrderItemId, ct);
        var line = context == null ? null : await feedback.FindItemAsync(ActorId, form.OrderItemId, ct);
        return context == null || line == null ? null : new ReviewPage(context, line, form);
    }

    // Re-render only for an order line the actor owns; otherwise return the bare status without any order context.
    private async Task<IActionResult> Redisplay(ReviewForm form, int status, string? error, CancellationToken ct)
    {
        var page = form.OrderItemId > 0 ? await Load(form, ct) : null;
        if (page == null) return StatusCode(status);
        Response.StatusCode = status;
        ViewData["ReviewError"] = error;
        return View("Create", page);
    }
}
