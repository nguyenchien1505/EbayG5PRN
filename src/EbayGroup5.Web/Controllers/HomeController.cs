using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class HomeController(EbayGroup5.Application.Catalog.ICatalogService catalog) : Controller
{
    // H03 (reference HM): eBay home with real categories and the newest listings; no invented deals or prices.
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Electronics, Cars, Fashion, Collectibles & More";
        ViewData["Recent"] = await catalog.RecentAsync(12, cancellationToken);
        return View(await catalog.CategoriesAsync(cancellationToken));
    }

    [HttpGet]
    public IActionResult Search(string? q) => RedirectToAction("Index", "Catalog", new { q });

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status(int code)
    {
        // F03: friendly pages for 404 and 403 (browser GETs); other codes keep the bare status.
        if (code == 403)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("Forbidden");
        }
        if (code != 404) return StatusCode(code is >= 400 and <= 599 ? code : 404);
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
