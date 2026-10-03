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

namespace EbayGroup5.Database.Tests;

public sealed class AccountHttpTests
{
    private sealed class Factory(string target) : WebApplicationFactory<EbayGroup5.Web.Controllers.AccountController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<EbayDbContext>>();
                services.RemoveAll<EbayDbContext>();
                services.AddDbContext<EbayDbContext>(options => options.UseSqlServer(target));
            });
        }
    }

    private static async Task<string> Token(HttpClient client)
    {
        var html = await client.GetStringAsync("/Account/Register");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Antiforgery field missing.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
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
