using System.Security.Claims;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Web.Models;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EbayGroup5.Infrastructure.Persistence;
using EbayGroup5.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(AccountService accounts) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginForm { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });

    [HttpPost, SharedRateLimit(SharedRateLimiter.Auth)]
    public async Task<IActionResult> Login(LoginForm form)
    {
        var account = ModelState.IsValid ? await accounts.LoginAsync(form.Email, form.Password) : null;
        if (account == null)
        {
            ModelState.Clear();
            ModelState.AddModelError("", "Unable to sign in. Check your email and password.");
            form.Password = "";
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return View(form);
        }
        var identity = new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, account.Role)
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
        return Url.IsLocalUrl(form.ReturnUrl) ? LocalRedirect(form.ReturnUrl!) : RedirectToAction(nameof(Me));
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterForm());

    [HttpPost, SharedRateLimit(SharedRateLimiter.Auth)]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        if (ModelState.IsValid && await accounts.RegisterAsync(form.Name, form.Email, form.Password))
        {
            TempData["AccountNotice"] = "Your account has been created. Sign in to continue.";
            return RedirectToAction(nameof(Login));
        }
        ModelState.Remove(nameof(form.Password));
        form.Password = "";
        ModelState.AddModelError("", "Unable to create an account. Check your details; use a password of 12–128 characters or sign in if you already registered.");
        return View(form);
    }

    [Authorize, HttpGet]
    public IActionResult Me() => View();

    [Authorize, HttpGet]
    public async Task<IActionResult> Details(int id, [FromServices] EbayDbContext db, [FromServices] IAuthorizationService authorization)
    {
        var account = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, HttpContext.RequestAborted);
        if (account == null || !(await authorization.AuthorizeAsync(User, account, "AccountOwner")).Succeeded)
            return NotFound();
        return View("Me");
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
