using System.Security.Claims;
using EbayGroup5.Application.Orders;
using EbayGroup5.Application.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

[Authorize(Roles = "Buyer,Seller")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrdersController(IOrderService orders, IReviewService reviews) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private OrderAudience Audience => User.IsInRole("Seller") ? OrderAudience.Seller : OrderAudience.Buyer;

    [HttpGet, Authorize(Policy = "Buyer")]
    public Task<IActionResult> Purchases(int page = 1, string? status = null, CancellationToken cancellationToken = default) => History(OrderAudience.Buyer, page, status, cancellationToken);
    [HttpGet, Authorize(Policy = "Seller")]
    public Task<IActionResult> Sales(int page = 1, string? status = null, CancellationToken cancellationToken = default) => History(OrderAudience.Seller, page, status, cancellationToken);

    private async Task<IActionResult> History(OrderAudience audience, int page, string? status, CancellationToken ct)
    {
        if (!ModelState.IsValid || page < 1 || page > int.MaxValue / OrderHistory.PageSize || (status != null && status is not ("Pending" or "Shipped" or "Delivered" or "Cancelled"))) return BadRequest();
        return View("History", await orders.HistoryAsync(ActorId, audience, page, status, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(ActorId, Audience, id, cancellationToken);
        return order == null ? NotFound() : await DetailsView(order, cancellationToken);
    }

    private async Task<IActionResult> DetailsView(OrderDetail order, CancellationToken ct)
    {
        if (order.Audience == OrderAudience.Buyer) ViewData["ReviewLines"] = await reviews.ListForOrderAsync(ActorId, order.Id, ct);
        return View("Details", order);
    }

    [HttpPost, Authorize(Policy = "Seller")]
    public Task<IActionResult> Ship(int id, CancellationToken cancellationToken) => Act(OrderAction.Ship, id, cancellationToken);
    [HttpPost, Authorize(Policy = "Buyer")]
    public Task<IActionResult> Receive(int id, CancellationToken cancellationToken) => Act(OrderAction.Receive, id, cancellationToken);

    private async Task<IActionResult> Act(OrderAction action, int id, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await orders.ActAsync(ActorId, action, id, ct);
        if (result.Outcome == OrderOutcome.Success) return RedirectToAction(nameof(Details), new { id });
        if (result.Outcome == OrderOutcome.NotFound) return NotFound();
        if (result.Outcome == OrderOutcome.Forbidden) return StatusCode(403);
        Response.StatusCode = 409;
        ViewData["OrderError"] = result.Error;
        var order = await orders.FindAsync(ActorId, Audience, id, ct);
        return order == null ? NotFound() : await DetailsView(order, ct);
    }
}
