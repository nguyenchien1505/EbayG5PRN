using System.Net;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>F02: shared key ring and shared rate limits across instances (SCALE_OUT.md, OPS-01/OPS-02).</summary>
public sealed class ScaleOutTests
{
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Redis => Environment.GetEnvironmentVariable("EBAY_TEST_REDIS") ?? throw new InvalidOperationException("Run Test-CI.ps1 or Test-Database.ps1; Redis is required.");
    private static string Prefix() => "EbayGroup5.Tests.F02." + Guid.NewGuid().ToString("N");

    private static (SharedRateLimiter Limiter, RedisConnection Connection) Limiter(string prefix, TimeProvider clock, string? redis = null, int authLimit = 5)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = redis ?? Redis, ["Redis:KeyPrefix"] = prefix,
            ["RateLimits:auth:PermitLimit"] = authLimit.ToString(), ["RateLimits:auth:WindowSeconds"] = "60"
        }).Build();
        var connection = new RedisConnection(configuration);
        return (new SharedRateLimiter(connection, configuration, clock, NullLogger<SharedRateLimiter>.Instance), connection);
    }

    [Fact]
    public async Task Redis_counters_are_shared_between_instances_per_policy_partition_and_window()
    {
        var prefix = Prefix();
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)); // Start of a 60 s window.
        var (a, connectionA) = Limiter(prefix, clock);
        var (b, connectionB) = Limiter(prefix, clock);
        await using (connectionA)
        await using (connectionB)
        {
            var allowed = 0;
            for (var i = 0; i < 10; i++) if ((await (i % 2 == 0 ? a : b).AcquireAsync(SharedRateLimiter.Auth, "ip:203.0.113.7")).Allowed) allowed++;
            Assert.Equal(5, allowed); // Two "instances", one budget.
            var denied = await a.AcquireAsync(SharedRateLimiter.Auth, "ip:203.0.113.7");
            Assert.False(denied.Allowed);
            Assert.InRange(denied.RetryAfter, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(60));
            Assert.True((await b.AcquireAsync(SharedRateLimiter.Auth, "ip:203.0.113.8")).Allowed); // Other partition.
            Assert.True((await b.AcquireAsync(SharedRateLimiter.ChatApi, "ip:203.0.113.7")).Allowed); // Other policy.
            clock.Now += TimeSpan.FromSeconds(60);
            Assert.True((await a.AcquireAsync(SharedRateLimiter.Auth, "ip:203.0.113.7")).Allowed); // Next window.
            await Assert.ThrowsAsync<ArgumentException>(() => a.AcquireAsync("bogus", "ip:1"));
        }
        var bad = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimits:chat-hub:PermitLimit"] = "0" }).Build();
        Assert.Throws<InvalidOperationException>(() => new SharedRateLimiter(new RedisConnection(bad), bad, TimeProvider.System, NullLogger<SharedRateLimiter>.Instance));
    }

    [Fact]
    public async Task Unreachable_Redis_falls_back_to_per_instance_limits_without_failing_open()
    {
        var port = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        port.Start(); var unused = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var (limiter, connection) = Limiter(Prefix(), TimeProvider.System, $"127.0.0.1:{unused},abortConnect=false,connectRetry=0,connectTimeout=250,asyncTimeout=250", authLimit: 3);
        await using (connection)
        {
            var started = DateTime.UtcNow;
            var results = new List<bool>();
            for (var i = 0; i < 5; i++) results.Add((await limiter.AcquireAsync(SharedRateLimiter.Auth, "ip:198.51.100.1")).Allowed);
            Assert.Equal(new[] { true, true, true, false, false }, results);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10)); // No long Redis timeouts per request.
        }
    }

    private static async Task<string> LoginCookie(AccountHttpTests.Factory factory)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost") });
        var page = await client.GetAsync("/Account/Login");
        var antiforgery = string.Join("; ", page.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]));
        using var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["Email"] = "buyer@d01.example.invalid", ["Password"] = Password, ["__RequestVerificationToken"] = Field(await page.Content.ReadAsStringAsync()) })
        };
        post.Headers.Add("Cookie", antiforgery);
        var login = await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("EbayGroup5.Auth=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<HttpResponseMessage> Me(AccountHttpTests.Factory factory, string cookie)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Me");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Login_survives_switching_instance_only_with_the_shared_key_ring()
    {
        var s = await Setup();
        var shared = new Dictionary<string, string> { ["DataProtection:KeyStore"] = "Redis", ["Redis:KeyPrefix"] = Prefix() };
        using var a = new AccountHttpTests.Factory(s.Target, settings: shared);
        using var b = new AccountHttpTests.Factory(s.Target, settings: shared);
        using var isolated = new AccountHttpTests.Factory(s.Target, settings: new Dictionary<string, string> { ["DataProtection:KeyStore"] = "Redis", ["Redis:KeyPrefix"] = Prefix() });
        var cookie = await LoginCookie(a);
        Assert.Equal(HttpStatusCode.OK, (await Me(a, cookie)).StatusCode);
        var other = await Me(b, cookie);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode); // Another instance reads the same key ring.
        Assert.Contains("buyer", await other.Content.ReadAsStringAsync());
        var control = await Me(isolated, cookie);
        Assert.Equal(HttpStatusCode.Redirect, control.StatusCode); // A different key ring cannot read it.
        Assert.StartsWith("/Account/Login", control.Headers.Location!.OriginalString.Replace("https://localhost", ""));
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var wrong = new AccountHttpTests.Factory(s.Target, settings: new Dictionary<string, string> { ["DataProtection:KeyStore"] = "Disk" });
            wrong.CreateClient().GetAsync("/Account/Login").GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Auth_limit_is_shared_by_two_instances_and_chat_api_returns_problem_details()
    {
        var s = await Setup();
        var shared = new Dictionary<string, string> { ["Redis:KeyPrefix"] = Prefix() };
        using var a = new AccountHttpTests.Factory(s.Target, settings: shared);
        using var b = new AccountHttpTests.Factory(s.Target, settings: shared);
        using var clientA = a.CreateClient(new() { AllowAutoRedirect = false });
        using var clientB = b.CreateClient(new() { AllowAutoRedirect = false });
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await (i % 2 == 0 ? clientA : clientB).PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        var limited = await clientB.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.InRange(int.Parse(limited.Headers.GetValues("Retry-After").Single()), 1, 60);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientA.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);

        using var chat = new AccountHttpTests.Factory(s.Target, settings: new Dictionary<string, string> { ["RateLimits:chat-api:PermitLimit"] = "2" });
        using var buyer = await Login(chat, "buyer");
        for (var i = 0; i < 2; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await buyer.PostAsync("/api/v1/chat/conversations/1/read", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        var tooMany = await buyer.PostAsync("/api/v1/chat/conversations/1/read", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.TooManyRequests, tooMany.StatusCode);
        Assert.Contains("Too many requests.", await tooMany.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync("/api/v1/chat/conversations")).StatusCode); // Reads are not limited.
    }

    [Fact]
    public async Task Hub_invocations_share_the_user_budget_across_hosts_and_recover_next_window()
    {
        var f = await ChatFixture.Create();
        var keys = new EphemeralDataProtectionProvider();
        var limits = new Dictionary<string, string?> { ["RateLimits:chat-hub:PermitLimit"] = "3", ["RateLimits:chat-hub:WindowSeconds"] = "2" };
        await using var first = await ChatSocketHost.Start(f, keys, settings: limits);
        await using var second = await ChatSocketHost.Start(f, keys, settings: limits);
        var buyerOnFirst = await first.Login(0);
        var id = await buyerOnFirst.Open(f.Product);
        // Start right after a window boundary so the burst stays inside one 2-second window.
        while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 2000 > 300) await Task.Delay(50);
        await buyerOnFirst.Connect();
        var buyerOnSecond = await second.Login(0);
        await buyerOnSecond.Connect();
        await buyerOnFirst.Join(id);
        await buyerOnSecond.Join(id);
        await buyerOnFirst.Join(id);
        var exception = await Assert.ThrowsAsync<HubException>(() => buyerOnSecond.Join(id)); // Fourth call on any host.
        Assert.Contains("HubException: 429:", exception.Message);
        Assert.Equal(HubConnectionState.Connected, buyerOnSecond.Hub!.State); // Throttled, not disconnected.
        await Task.Delay(2100);
        Assert.Equal(id, (await buyerOnSecond.Join(id)).Id);
    }
}
