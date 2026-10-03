using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Search(string? q)
    {
        var query = (q ?? "").Trim();
        if (query.Length > 100)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ViewData["SearchError"] = "Use 100 characters or fewer to search.";
            query = query[..100];
        }
        ViewData["SearchQuery"] = query;
        return View();
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status(int code)
    {
        if (code != 404) return StatusCode(code is >= 400 and <= 599 ? code : 404);
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
