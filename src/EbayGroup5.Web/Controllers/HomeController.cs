using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Catalog");

    [HttpGet]
    public IActionResult Search(string? q) => RedirectToAction("Index", "Catalog", new { q });

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status(int code)
    {
        if (code != 404) return StatusCode(code is >= 400 and <= 599 ? code : 404);
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
