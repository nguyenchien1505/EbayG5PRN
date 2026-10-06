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

    [HttpPost]
    public async Task<IActionResult> Create([FromForm] ReviewForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await reviews.SubmitAsync(ActorId, form.OrderItemId, form.Rating, form.Comment, cancellationToken);
        return result.Outcome switch
        {
            ReviewOutcome.Success => RedirectToAction("Details", "Orders", new { id = result.OrderId }),
            ReviewOutcome.Invalid => Problem(result.Error, statusCode: StatusCodes.Status400BadRequest),
            ReviewOutcome.NotFound => NotFound(),
            ReviewOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            _ => Problem(result.Error, statusCode: StatusCodes.Status409Conflict)
        };
    }
}
