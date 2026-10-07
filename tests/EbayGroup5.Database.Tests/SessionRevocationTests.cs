using System.Net;
using System.Security.Claims;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>F04 / Q-SEC-001 (SECURITY_TESTS.md): logout revokes that session everywhere, including copies of its cookie.</summary>
public sealed class SessionRevocationTests
{
    private static string Redis => Environment.GetEnvironmentVariable("EBAY_TEST_REDIS") ?? throw new InvalidOperationException("Run Test-CI.ps1 or Test-Database.ps1; Redis is required.");

    private static HttpClient Bare(AccountHttpTests.Factory factory) =>
        factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost") });

    private static string CookiePairs(HttpResponseMessage response) => string.Join("; ",
        (response.Headers.TryGetValues("Set-Cookie", out var values) ? values : []).Select(x => x.Split(';')[0]).Where(x => !x.EndsWith('=')));

    /// <summary>Signs in and returns just the auth cookie ("EbayGroup5.Auth=..."), as someone copying it from a browser would.</summary>
    private static async Task<string> SignIn(AccountHttpTests.Factory factory, string who)
    {
        using var client = Bare(factory);
        var page = await client.GetAsync("/Account/Login");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = who + "@d01.example.invalid", ["Password"] = Password, ["__RequestVerificationToken"] = Field(await page.Content.ReadAsStringAsync()) })
        };
        login.Headers.Add("Cookie", CookiePairs(page));
        var response = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return CookiePairs(response).Split("; ").Single(x => x.StartsWith("EbayGroup5.Auth=", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> Get(AccountHttpTests.Factory factory, string cookie, string url)
    {
        using var client = Bare(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<bool> SignedIn(AccountHttpTests.Factory factory, string cookie) =>
        (await Get(factory, cookie, "/Account/Me")).StatusCode == HttpStatusCode.OK;

    private static async Task Logout(AccountHttpTests.Factory factory, string cookie)
    {
        var me = await Get(factory, cookie, "/Account/Me");
        using var client = Bare(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Field(await me.Content.ReadAsStringAsync()) })
        };
        request.Headers.Add("Cookie", cookie + "; " + CookiePairs(me));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Copied_cookie_stops_working_on_every_instance_after_logout_while_other_sessions_continue()
    {
        var s = await Setup();
        // Two app instances sharing the Redis key ring and revocation list (F02 deployment shape).
        var shared = new Dictionary<string, string> { ["DataProtection:KeyStore"] = "Redis", ["Redis:KeyPrefix"] = "EbayGroup5.Tests.F04." + Guid.NewGuid().ToString("N") };
        using var first = new AccountHttpTests.Factory(s.Target, settings: shared);
        using var second = new AccountHttpTests.Factory(s.Target, settings: shared);
        var session = await SignIn(first, "buyer");
        var otherSession = await SignIn(second, "buyer");
        Assert.True(await SignedIn(first, session) && await SignedIn(second, session), "the copied cookie works on both instances before logout");

        await Logout(first, session);

        Assert.False(await SignedIn(first, session), "copied cookie still works on the instance that logged out");
        Assert.False(await SignedIn(second, session), "copied cookie still works on the other instance");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(second, session, "/api/v1/chat/conversations")).StatusCode);
        Assert.True(await SignedIn(first, otherSession) && await SignedIn(second, otherSession), "logout ended the user's other session");
        // Logging in again gives a fresh session.
        Assert.True(await SignedIn(second, await SignIn(first, "buyer")));
    }

    [Fact]
    public async Task Validly_signed_cookie_without_a_session_id_is_refused()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        _ = factory.Server; // build the host so its key ring signs the tickets below
        var format = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        string Cookie(params Claim[] extra)
        {
            var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, s.Buyer.ToString()), new(ClaimTypes.Name, "buyer"), new(ClaimTypes.Role, "Buyer"), .. extra],
                CookieAuthenticationDefaults.AuthenticationScheme);
            var now = DateTimeOffset.UtcNow;
            return "EbayGroup5.Auth=" + format.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity),
                new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddMinutes(30) }, CookieAuthenticationDefaults.AuthenticationScheme));
        }
        Assert.True(await SignedIn(factory, Cookie(SessionRevocation.NewClaim())), "control: a minted ticket with a session id is accepted");
        Assert.False(await SignedIn(factory, Cookie()), "a cookie issued without a session id was accepted");
        Assert.False(await SignedIn(factory, Cookie(new Claim(SessionRevocation.ClaimType, "not-a-session"))), "a malformed session id was accepted");
    }

    [Fact]
    public async Task Open_chat_connection_is_cut_at_its_next_call_after_logout()
    {
        var f = await ChatFixture.Create();
        await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider());
        var buyer = await host.Login(0);
        var conversation = await buyer.Open(f.Product);
        await buyer.Connect();
        await buyer.Join(conversation);
        var me = await buyer.Http.GetStringAsync("/Account/Me");
        var logout = await buyer.Http.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Field(me) }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var before = SqlFixture.Scalar(f.Target, "SELECT COUNT(*) FROM dbo.Message");
        await Assert.ThrowsAnyAsync<Exception>(() => buyer.Send(conversation, new(Guid.NewGuid(), "after logout")));
        await buyer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(before, SqlFixture.Scalar(f.Target, "SELECT COUNT(*) FROM dbo.Message"));
    }

    [Fact]
    public async Task Unreachable_Redis_keeps_revocations_on_the_logging_out_instance_without_failing()
    {
        var port = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        port.Start(); var unused = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = $"127.0.0.1:{unused},abortConnect=false,connectRetry=0,connectTimeout=250,asyncTimeout=250",
            ["Redis:KeyPrefix"] = "EbayGroup5.Tests.F04." + Guid.NewGuid().ToString("N")
        }).Build();
        await using var connection = new RedisConnection(configuration);
        var down = new SessionRevocation(connection, TimeProvider.System, NullLogger<SessionRevocation>.Instance);
        var revoked = SessionRevocation.NewClaim().Value;
        var other = SessionRevocation.NewClaim().Value;
        await down.RevokeAsync(revoked, DateTimeOffset.UtcNow.AddMinutes(30));
        Assert.True(await down.IsRevokedAsync(revoked));
        Assert.False(await down.IsRevokedAsync(other));

        // With Redis up, a second instance sees the first one's revocation; an expired revocation is not stored.
        var live = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Redis"] = Redis, ["Redis:KeyPrefix"] = "EbayGroup5.Tests.F04." + Guid.NewGuid().ToString("N") }).Build();
        await using var shared = new RedisConnection(live);
        var a = new SessionRevocation(shared, TimeProvider.System, NullLogger<SessionRevocation>.Instance);
        var b = new SessionRevocation(shared, TimeProvider.System, NullLogger<SessionRevocation>.Instance);
        await a.RevokeAsync(revoked, DateTimeOffset.UtcNow.AddMinutes(30));
        await a.RevokeAsync(other, DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.True(await b.IsRevokedAsync(revoked));
        Assert.False(await b.IsRevokedAsync(other));
        var ttl = await shared.Multiplexer.GetDatabase().KeyTimeToLiveAsync($"{live["Redis:KeyPrefix"]}:revoked-session:{revoked}");
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }
}
