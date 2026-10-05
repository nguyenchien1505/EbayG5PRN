using EbayGroup5.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class CatalogController(ICatalogService catalog) : Controller
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
        return result == null ? NotFound() : View(result);
    }
}
