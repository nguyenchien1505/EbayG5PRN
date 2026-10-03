using System.Net;
using System.Text.RegularExpressions;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Database.Tests;

public sealed class AccountHttpTests
{
    private sealed class Factory(string target, string? remoteIp = "127.0.0.1") : WebApplicationFactory<EbayGroup5.Web.Controllers.AccountController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<EbayDbContext>>();
                services.RemoveAll<EbayDbContext>();
                services.AddDbContext<EbayDbContext>(options => options.UseSqlServer(target));
                services.AddSingleton<IStartupFilter>(new RemoteAddress(remoteIp));
                services.AddControllers().AddApplicationPart(typeof(SecurityProbeController).Assembly);
            });
        }
    }

    private sealed class RemoteAddress(string? address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = address == null ? null : IPAddress.Parse(address);
                await continuation(context);
            });
            next(app);
        };
    }

    private static async Task SignIn(HttpClient client)
    {
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "security@example.invalid", ["Password"] = "Security-Test-Password!",
            ["__RequestVerificationToken"] = await Token(client)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("password")]
    [InlineData("deleted")]
    public async Task Account_resource_denies_foreign_missing_and_stale_cookies(string change)
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        await using var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
        Assert.True(await new AccountService(db).RegisterAsync("Security Buyer", "security@example.invalid", "Security-Test-Password!"));
        Assert.True(await new AccountService(db).RegisterAsync("Other Buyer", "other@example.invalid", "Security-Test-Password!"));
        var own = await db.Set<User>().SingleAsync(x => x.Email == "SECURITY@EXAMPLE.INVALID");
        var other = await db.Set<User>().SingleAsync(x => x.Id != own.Id);
        using var factory = new Factory(target);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await SignIn(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Account/Details/{own.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Account/Details/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Account/Details/2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/SecurityProbe/Admin")).StatusCode);
        if (change == "role") own.Role = "Admin";
        else if (change == "password") own.Password = null;
        else db.Remove(own);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Me")).StatusCode);
    }

    [Fact]
    public async Task Auth_post_limit_is_shared_between_login_and_register_and_ignores_forwarded_ip()
    {
        using var factory = new Factory(SqlFixture.NewTarget());
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        for (var i = 0; i < 10; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{i}");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(i % 2 == 0 ? "/Account/Login" : "/Account/Register", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.0.2.1", false)]
    [InlineData(null, false)]
    public async Task Admin_access_requires_direct_allowed_address(string? address, bool allowed)
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        await using var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
        Assert.True(await new AccountService(db).RegisterAsync("Security Admin", "security@example.invalid", "Security-Test-Password!"));
        await db.Set<User>().ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, "Admin"));
        using var factory = new Factory(target, address);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        await SignIn(client);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/SecurityProbe/Admin")).StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/Account/Me")).StatusCode);
    }

    [Fact]
    public async Task Server_error_response_does_not_expose_exception()
    {
        var target = SqlFixture.NewTarget();
        using var factory = new Factory(target);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/SecurityProbe/Failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("An unexpected error occurred.", body);
        Assert.DoesNotContain("Internal sentinel", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> Token(HttpClient client)
    {
        var html = await client.GetStringAsync("/Account/Register");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Antiforgery field missing.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Fact]
    public async Task Shell_search_encodes_query_validates_length_and_preserves_http_errors()
    {
        using var factory = new Factory(SqlFixture.NewTarget());
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var query = "<script>alert(1)</script>";
        var search = await client.GetStringAsync("/Home/Search?q=" + Uri.EscapeDataString(query));
        Assert.Contains("Search is coming soon", search);
        Assert.Contains("&lt;script&gt;", search);
        Assert.DoesNotContain(query, search);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Home/Search?q=" + new string('a', 101))).StatusCode);
        var missing = await client.GetAsync("/Home/ThisPageDoesNotExist");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("We looked everywhere.", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }

    [Fact]
    public async Task Cookie_login_local_redirect_csrf_and_logout_work_over_http()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        await using (var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options))
            Assert.True(await new AccountService(db).RegisterAsync("HTTP Buyer", "http@example.invalid", "HTTP-Test-Password-789!"));
        using var factory = new Factory(target);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Me")).StatusCode);
        var fields = new Dictionary<string,string> { ["Email"]="http@example.invalid", ["Password"]="HTTP-Test-Password-789!", ["ReturnUrl"]="https://evil.example/", ["Role"]="Admin" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Login",new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"] = await Token(client);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Account/Me", login.Headers.Location!.OriginalString);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("EbayGroup5.Auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP-Test-Password", cookie);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Buyer", await client.GetStringAsync("/Account/Me"));
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await client.GetAsync("/Account/Logout")).StatusCode);
        var logout = await client.PostAsync("/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"]=await Token(client) }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Me")).StatusCode);
    }

    [Fact]
    public async Task Register_requires_csrf_ignores_role_and_never_echoes_password()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        using var factory = new Factory(target);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var fields = new Dictionary<string,string> { ["Name"]="Form Buyer",["Email"]="form@example.invalid", ["Password"]="Form-Test-Password-987!", ["Role"]="Admin" };
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Account/Register",new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"]=await Token(client);
        var registered = await client.PostAsync("/Account/Register",new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Equal("/Account/Login", registered.Headers.Location!.OriginalString);
        var duplicate = await client.PostAsync("/Account/Register",new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK,duplicate.StatusCode);
        Assert.DoesNotContain(fields["Password"],await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("Buyer",SqlFixture.Scalar(target,"SELECT role FROM dbo.[User]"));
        Assert.Equal("1",SqlFixture.Scalar(target,"SELECT COUNT(*) FROM dbo.[User]"));
    }

    [Fact]
    public async Task Login_errors_are_generic_and_login_ui_renders()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        using var factory = new Factory(target);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Contains("Welcome back!", await client.GetStringAsync("/Account/Login"));

        foreach (var email in new[] { "missing@example.invalid", "buyer.a06@example.invalid", "not-email" })
        {
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Email"]=email, ["Password"]="Private-Test-Password!", ["__RequestVerificationToken"]=await Token(client)
            }));
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Private-Test-Password",body);
            Assert.Contains("Unable to sign in. Check your email and password.", body);

        }
    }
}

// Available only through the test assembly application part, never shipped in Web.
public sealed class SecurityProbeController : Controller
{
    [Authorize(Policy = "AdminDemo"), HttpGet]
    public IActionResult Admin() => Ok();

    [HttpGet]
    public IActionResult Failure() => throw new InvalidOperationException("Internal sentinel");
}
