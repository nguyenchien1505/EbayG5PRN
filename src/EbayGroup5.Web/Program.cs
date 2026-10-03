using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EbayGroup5.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authorization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccessCookieEvents>();
builder.Services.AddSingleton<AdminNetwork>();
builder.Services.AddSingleton<IAuthorizationHandler, AccountOwnerHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "EbayGroup5.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.EventsType = typeof(AccessCookieEvents);
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AccountOwner", policy => policy.RequireAuthenticatedUser().AddRequirements(new AccountOwnerRequirement()));
    options.AddPolicy("Buyer", policy => policy.RequireRole("Buyer"));
    options.AddPolicy("Seller", policy => policy.RequireRole("Seller"));
    options.AddPolicy("AdminDemo", policy => policy.RequireRole("Admin"));
});
builder.Services.AddDbContext<EbayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required for database operations.")));

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(context =>
        Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.").ExecuteAsync(context)));

app.UseStaticFiles();
app.UseWhen(context => HttpMethods.IsGet(context.Request.Method), branch =>
    branch.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}"));
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.IsInRole("Admin") && !context.RequestServices.GetRequiredService<AdminNetwork>().Allows(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next(context);
});
app.UseAuthorization();

// Process liveness only. Database/Redis readiness is added with infrastructure.
app.MapGet("/health/live", () => Results.Text("Healthy", "text/plain"));
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;
