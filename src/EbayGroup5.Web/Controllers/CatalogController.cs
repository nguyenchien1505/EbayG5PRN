using EbayGroup5.Application.Catalog;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class CatalogController(ICatalogService catalog, IReviewService reviews) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, int? categoryId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || (q?.Trim().Length ?? 0) > 100 || categoryId is <= 0 || page <= 0 || page > int.MaxValue / CatalogPage.PageSize)
        {
            Response.StatusCode = 400;
            return View("InvalidFilters");
        }
        var result = await catalog.SearchAsync(q, categoryId, page, cancellationToken);
        ViewData["SearchQuery"] = result.Query;
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var result = await catalog.FindAsync(id, cancellationToken);
        if (result == null) return NotFound();
        ViewData["Reviews"] = await reviews.ProductReviewsAsync(id, cancellationToken: cancellationToken);
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Reviews(int id, int page = 1, string? sort = null, CancellationToken cancellationToken = default)
    {
        ReviewSort? order = (sort ?? "recent") switch { "recent" => ReviewSort.Recent, "highest" => ReviewSort.Highest, "lowest" => ReviewSort.Lowest, _ => null };
        if (!ModelState.IsValid || order == null || page <= 0 || page > int.MaxValue / ProductReviewPage.PageSize)
        {
            Response.StatusCode = 400;
            return View("InvalidReviewFilters", id);
        }
        var result = await catalog.FindAsync(id, cancellationToken);
        if (result == null) return NotFound();
        return View(new ProductReviewsView(result.Item, await reviews.ProductReviewsAsync(id, page, order.Value, cancellationToken)));
    }
}
