using System.Security.Claims;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

[Authorize(Policy = "Buyer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewsController(IReviewService reviews) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Create(int orderItemId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || orderItemId < 1) return BadRequest();
        var context = await reviews.FindItemAsync(ActorId, orderItemId, cancellationToken);
        return context == null ? NotFound() : View(new ReviewPage(context, new ReviewForm { OrderItemId = orderItemId }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromForm] ReviewForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await Redisplay(form, StatusCodes.Status400BadRequest, "Select a star rating from 1 to 5.", cancellationToken);
        var result = await reviews.SubmitAsync(ActorId, form.OrderItemId, form.Rating, form.Comment, cancellationToken);
        switch (result.Outcome)
        {
            case ReviewOutcome.Success:
                TempData["OrderNotice"] = "Thanks — your review was posted.";
                return RedirectToAction("Details", "Orders", new { id = result.OrderId });
            case ReviewOutcome.NotFound: return NotFound();
            case ReviewOutcome.Forbidden: return StatusCode(StatusCodes.Status403Forbidden);
            case ReviewOutcome.Invalid: return await Redisplay(form, StatusCodes.Status400BadRequest, result.Error, cancellationToken);
            default: return await Redisplay(form, StatusCodes.Status409Conflict, result.Error, cancellationToken);
        }
    }

    // Re-render only for an order line the actor owns; otherwise return the bare status without any order context.
    private async Task<IActionResult> Redisplay(ReviewForm form, int status, string? error, CancellationToken ct)
    {
        var context = form.OrderItemId > 0 ? await reviews.FindItemAsync(ActorId, form.OrderItemId, ct) : null;
        if (context == null) return StatusCode(status);
        Response.StatusCode = status;
        ViewData["ReviewError"] = error;
        return View("Create", new ReviewPage(context, form));
    }
}
