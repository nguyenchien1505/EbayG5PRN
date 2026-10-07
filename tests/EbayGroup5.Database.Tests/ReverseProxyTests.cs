using System.Net;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Web.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>F01: forwarded headers only from configured proxies, and readiness (DEPLOYMENT.md).</summary>
public sealed class ReverseProxyTests
{
    private const string Proxy = "172.30.250.10";
    private static readonly Dictionary<string, string> BehindNginx = new() { ["ReverseProxy:KnownProxies:0"] = Proxy };

    private static async Task<SetupData> WithAdmin()
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        Assert.True(await new AccountService(db).RegisterAsync("f01admin", "f01admin@d01.example.invalid", Password));
        await db.Set<User>().Where(x => x.Email == "F01ADMIN@D01.EXAMPLE.INVALID").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Admin"));
        return s;
    }

    private static async Task<HttpStatusCode> AdminPage(HttpClient admin, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Moderation");
        if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await admin.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Forwarded_for_is_used_only_from_the_configured_proxy()
    {
        var s = await WithAdmin();

        // No proxy configured: the header is ignored entirely (ASP.NET would otherwise trust every sender).
        using (var direct = new AccountHttpTests.Factory(s.Target))
        {
            using var admin = await Login(direct, "f01admin");
            Assert.Equal(HttpStatusCode.OK, await AdminPage(admin, "203.0.113.7"));
        }

        // Behind the configured proxy: the client address comes from X-Forwarded-For.
        using (var proxied = new AccountHttpTests.Factory(s.Target, Proxy, settings: BehindNginx))
        {
            using var admin = await Login(proxied, "f01admin");
            Assert.Equal(HttpStatusCode.Forbidden, await AdminPage(admin, "203.0.113.7"));
            Assert.Equal(HttpStatusCode.OK, await AdminPage(admin, "127.0.0.1"));
            Assert.Equal(HttpStatusCode.Forbidden, await AdminPage(admin, null)); // The proxy itself is not an admin address.
        }

        // Same header from an address that is not the proxy: ignored, so spoofing 127.0.0.1 does not help.
        using (var spoofed = new AccountHttpTests.Factory(s.Target, "192.0.2.10", settings: BehindNginx))
        {
            using var admin = await Login(spoofed, "f01admin");
            Assert.Equal(HttpStatusCode.Forbidden, await AdminPage(admin, "127.0.0.1"));
        }
    }

    [Fact]
    public void Proxy_configuration_is_validated_and_empty_means_disabled()
    {
        IConfiguration Config(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
        Assert.Null(ReverseProxy.Options(Config()));
        var options = ReverseProxy.Options(Config(("ReverseProxy:KnownProxies:0", Proxy), ("ReverseProxy:KnownNetworks:0", "10.10.0.0/16")))!;
        Assert.Equal(new[] { IPAddress.Parse(Proxy) }, options.KnownProxies);
        Assert.Equal((1, 16), (options.ForwardLimit!.Value, Assert.Single(options.KnownNetworks).PrefixLength));
        Assert.Equal(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        foreach (var bad in new[] { ("ReverseProxy:KnownProxies:0", "nginx"), ("ReverseProxy:KnownNetworks:0", "10.0.0.0"), ("ReverseProxy:KnownNetworks:0", "10.0.0.0/33") })
            Assert.Throws<InvalidOperationException>(() => ReverseProxy.Options(Config(bad)));
    }

    [Fact]
    public async Task Readiness_checks_sql_and_redis_without_details()
    {
        var s = await Setup();
        using (var factory = new AccountHttpTests.Factory(s.Target))
        using (var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }))
        {
            var ready = await client.GetAsync("/health/ready");
            Assert.Equal((HttpStatusCode.OK, "Ready"), (ready.StatusCode, await ready.Content.ReadAsStringAsync()));
            Assert.True(ready.Headers.CacheControl?.NoStore);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        var missing = new SqlConnectionStringBuilder(s.Target) { InitialCatalog = "EbayGroup5_Test_Missing" + Guid.NewGuid().ToString("N") }.ConnectionString;
        using (var factory = new AccountHttpTests.Factory(missing))
        using (var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }))
        {
            var notReady = await client.GetAsync("/health/ready");
            Assert.Equal((HttpStatusCode.ServiceUnavailable, "Not ready"), (notReady.StatusCode, await notReady.Content.ReadAsStringAsync()));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
    }
}
