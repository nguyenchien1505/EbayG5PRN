using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
