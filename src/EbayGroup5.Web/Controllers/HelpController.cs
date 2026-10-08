using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// ADR-003 §2: eBay controls outside the clone's scope open one honest notice instead of a dead link. Fixed keys only.
public sealed class HelpController : Controller
{
    internal static readonly IReadOnlyDictionary<string, string> Features = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["save-seller"] = "Save seller",
    };

    [HttpGet]
    public IActionResult Unavailable(string id)
    {
        if (!Features.TryGetValue(id ?? "", out var feature)) return NotFound();
        ViewData["Feature"] = feature;
        return View();
    }
}
