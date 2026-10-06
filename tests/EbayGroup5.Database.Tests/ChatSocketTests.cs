using System.Net;
using System.Net.Http.Json;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class ChatSocketTests
{
    private static async Task Denied(string code, Func<Task> invoke)
    {
        var exception = await Assert.ThrowsAsync<HubException>(invoke);
        Assert.Contains("HubException: " + code + ":", exception.Message);
        Assert.DoesNotContain("Private content", exception.Message);
    }

    [Fact]
    public async Task Two_independent_Kestrel_hosts_exchange_committed_messages_and_reads_over_Redis_and_REST()
    {
        var f = await ChatFixture.Create(); var keys = new EphemeralDataProtectionProvider();
        await using var a = await ChatSocketHost.Start(f, keys); await using var b = await ChatSocketHost.Start(f, keys);
        Assert.NotEqual(a.Address, b.Address);
        var buyer = await a.Login(0); var seller = await b.Login(2);
        var id = await buyer.Open(f.Product); await buyer.Connect(); await seller.Connect();
        Assert.Equal(HttpStatusCode.OK, (await buyer.Http.GetAsync(new Uri(b.Address, "/api/v1/chat/conversations"))).StatusCode);
        var joined = await seller.Join(id); Assert.Equal(f.Seller, joined.SellerId); Assert.Equal("0", joined.LastSequence);
        await buyer.Join(id);
        var command = new ChatSendCommand(Guid.NewGuid(), "Private content through Redis", f.Product);
        var receipt = await buyer.Send(id, command);
        var received = await seller.NextMessage(); Assert.Equal(receipt.Message, received); Assert.Equal(receipt.Message, await buyer.NextMessage());
        // Independent SQL connection sees the durable row/counter when the live payload is received.
        Assert.Equal("1|1", SqlFixture.Scalar(f.Target, $"SELECT CONCAT(LastSequence,'|',(SELECT COUNT(*) FROM dbo.Message WHERE ConversationId={id})) FROM dbo.ChatConversation WHERE Id={id}"));
        Assert.Equal(f.Buyer, received.SenderId); Assert.Equal(f.Seller, received.ReceiverId); Assert.False(receipt.Replayed);
        Assert.True((await buyer.Send(id, command)).Replayed);
        await seller.AssertQuiet(); await buyer.AssertQuiet();
        await Denied("409", () => buyer.Send(id, command with { Content = "Private content changed" }));
        var read = await seller.Hub!.InvokeAsync<ChatReadReceipt>("MarkRead", id, "1");
        Assert.True(read.Advanced); Assert.Equal(f.Seller, read.ReaderId);
        Assert.Equal(read, await buyer.NextRead()); Assert.Equal(read, await seller.NextRead());
        var repeated = await seller.Hub!.InvokeAsync<ChatReadReceipt>("MarkRead", id, "0");
        Assert.False(repeated.Advanced); Assert.Equal("1", repeated.ThroughSequence);
        await buyer.AssertQuiet(reads: true); await seller.AssertQuiet(reads: true);
        // REST sends use the same postcommit publisher, not an unrelated persistence path.
        var response = await buyer.Http.PostAsJsonAsync($"/api/v1/chat/conversations/{id}/messages", new ChatSendCommand(Guid.NewGuid(), "REST to WS"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rest = (await response.Content.ReadFromJsonAsync<ChatSendReceipt>())!;
        Assert.Equal("2", rest.Message.Sequence); Assert.Equal(rest.Message, await seller.NextMessage()); await buyer.NextMessage();
        var restRead = await buyer.Http.PostAsJsonAsync($"/api/v1/chat/conversations/{id}/read", new { throughSequence = "2" });
        var advanced = (await restRead.Content.ReadFromJsonAsync<ChatReadReceipt>())!;
        Assert.Equal(advanced, await seller.NextRead()); Assert.Equal(advanced, await buyer.NextRead()); Assert.Equal(f.Buyer, advanced.ReaderId);
        var history = await seller.Http.GetFromJsonAsync<ChatHistoryPage>($"/api/v1/chat/conversations/{id}/messages?after=0");
        Assert.Equal(new[] { "1", "2" }, history!.Messages.Select(x => x.Sequence));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("https://127.0.0.1:5085")]
    [InlineData("http://127.0.0.1:5086")]
    [InlineData("http://127.0.0.1:5085/")]
    [InlineData("http://127.0.0.1:5085/path")]
    [InlineData("http://127.0.0.1:5085 http://evil.example")]
    [InlineData("http://evil.example")]
    public async Task WS_requires_exact_explicit_origin_even_with_valid_cookie(string? origin)
    {
        var f = await ChatFixture.Create(); await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider());
        var buyer = await host.Login(0);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => buyer.Connect(origin));
        Assert.Contains("403", error.ToString());
        Assert.Equal("0", SqlFixture.Scalar(f.Target, "SELECT COUNT(*) FROM dbo.Message"));
    }

    [Fact]
    public async Task Handshake_requires_cookie_role_and_WS_without_negotiate_SSE_or_query_token()
    {
        var f = await ChatFixture.Create(); await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider());
        using var anonymous = host.Anonymous(); anonymous.DefaultRequestHeaders.Add("Origin", ChatSocketHost.Origin);
        var noCookie = await anonymous.GetAsync("/hubs/chat");
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode); Assert.Null(noCookie.Headers.Location); Assert.True(noCookie.Headers.CacheControl!.NoStore);
        var buyer = await host.Login(0); buyer.Http.DefaultRequestHeaders.Add("Origin", ChatSocketHost.Origin);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.Http.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", new StringContent(""))).StatusCode);
        buyer.Http.DefaultRequestHeaders.Add("Accept", "text/event-stream");
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.Http.GetAsync("/hubs/chat")).StatusCode);
        var queryToken = await Assert.ThrowsAnyAsync<Exception>(() => buyer.Connect(path: "/hubs/chat?access_token=ignored-token"));
        Assert.Contains("400", queryToken.ToString());
        var admin = await host.Login(4); var deniedAdmin = await Assert.ThrowsAnyAsync<Exception>(() => admin.Connect());
        Assert.Contains("403", deniedAdmin.ToString());
    }

    [Fact]
    public async Task Hub_rejects_third_party_objects_and_ignores_client_identity_fields()
    {
        var f = await ChatFixture.Create(); await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider());
        var buyer = await host.Login(0); var seller = await host.Login(2); var third = await host.Login(1);
        var id = await buyer.Open(f.Product); await buyer.Connect(); await seller.Connect(); await third.Connect();
        await buyer.Join(id); await seller.Join(id);
        await Denied("404", () => third.Join(id));
        await Denied("404", () => third.Send(id, new(Guid.NewGuid(), "Private content forbidden")));
        await Denied("404", () => third.Hub!.InvokeAsync<ChatReadReceipt>("MarkRead", id, "0"));
        await Denied("404", () => buyer.Join(int.MaxValue));
        await Assert.ThrowsAsync<HubException>(() => third.Hub!.InvokeAsync("Register", f.Buyer));
        var forged = await buyer.Hub!.InvokeAsync<ChatSendReceipt>("SendMessage", id, new { clientMessageId = Guid.NewGuid(), content = "server identity",
            senderId = f.OtherBuyer, actorId = f.Admin, receiverId = f.OtherSeller, sequence = "1000", traceId = "client", productId = f.Product, price = .01m });
        Assert.Equal(f.Buyer, forged.Message.SenderId); Assert.Equal(f.Seller, forged.Message.ReceiverId); Assert.Equal("1", forged.Message.Sequence);
        Assert.Equal(12.34m, forged.Message.Context!.UnitPrice); Assert.NotEqual("client", forged.Message.TraceId);
        Assert.Equal(forged.Message, await seller.NextMessage()); await buyer.NextMessage(); await third.AssertQuiet();
        Assert.Equal("1", SqlFixture.Scalar(f.Target, "SELECT COUNT(*) FROM dbo.Message"));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("password")]
    public async Task Current_SQL_role_and_credential_guard_excludes_stale_remote_subscribers_and_aborts_invocations(string change)
    {
        var f = await ChatFixture.Create(); var keys = new EphemeralDataProtectionProvider();
        await using var a = await ChatSocketHost.Start(f, keys); await using var b = await ChatSocketHost.Start(f, keys);
        var buyer = await a.Login(0); var seller = await b.Login(2); var id = await buyer.Open(f.Product);
        await buyer.Connect(); await seller.Connect(); await buyer.Join(id); await seller.Join(id);
        var command = new ChatSendCommand(Guid.NewGuid(), "before revocation");
        var initial = await buyer.Send(id, command); await buyer.NextMessage(); await seller.NextMessage();
        await using (var db = f.Open())
        {
            if (change == "role") await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Buyer"));
            else await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, (string?)null));
        }
        Assert.Equal(initial.Message, (await buyer.Send(id, command)).Message);
        await Denied("409", () => buyer.Send(id, new(Guid.NewGuid(), "new send after revocation")));
        var read = await buyer.Hub!.InvokeAsync<ChatReadReceipt>("MarkRead", id, "1");
        Assert.Equal(read, await buyer.NextRead()); await seller.AssertQuiet(reads: true); await seller.AssertQuiet();
        var stale = await Assert.ThrowsAnyAsync<Exception>(() => seller.Join(id));
        Assert.DoesNotContain("before revocation", stale.Message);
        await seller.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("1", SqlFixture.Scalar(f.Target, "SELECT COUNT(*) FROM dbo.Message"));
    }

    [Fact]
    public async Task Rotated_credential_groups_do_not_deliver_to_old_subscriptions_until_authorized_rejoin()
    {
        var f = await ChatFixture.Create(); var keys = new EphemeralDataProtectionProvider();
        await using var a = await ChatSocketHost.Start(f, keys); await using var b = await ChatSocketHost.Start(f, keys);
        var buyer = await a.Login(0); var seller = await b.Login(2); var id = await buyer.Open(f.Product);
        await buyer.Connect(); await seller.Connect(); await buyer.Join(id); await seller.Join(id);
        await buyer.Send(id, new(Guid.NewGuid(), "first")); await buyer.NextMessage(); await seller.NextMessage();
        await using (var db = f.Open())
            await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, AccountService.HashPassword("Rotated-Chat-Test-789!")));
        await buyer.Send(id, new(Guid.NewGuid(), "credential changed")); await buyer.NextMessage(); await seller.AssertQuiet();
        // B02 does not globally revoke copied cookies on password changes; a currently valid principal can explicitly rejoin.
        await seller.Join(id);
        var receipt = await buyer.Send(id, new(Guid.NewGuid(), "after rejoin")); await buyer.NextMessage();
        Assert.Equal(receipt.Message, await seller.NextMessage());
        var catchup = await seller.Http.GetFromJsonAsync<ChatHistoryPage>($"/api/v1/chat/conversations/{id}/messages?after=1");
        Assert.Equal(new[] { "2", "3" }, catchup!.Messages.Select(x => x.Sequence));
    }

    [Fact]
    public async Task Expired_cookie_closes_live_connection_and_HTTP_requires_login_again()
    {
        var f = await ChatFixture.Create(); await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider(), cookieSeconds: 4);
        var buyer = await host.Login(0); var id = await buyer.Open(f.Product); await buyer.Connect(); await buyer.Join(id);
        await buyer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await buyer.Http.GetAsync($"/api/v1/chat/conversations/{id}/messages")).StatusCode);
    }

    [Fact]
    public async Task Hub_rejects_bad_binding_invalid_text_and_oversize_frames_without_SQL_writes()
    {
        var f = await ChatFixture.Create(); await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider());
        var buyer = await host.Login(0); var id = await buyer.Open(f.Product); await buyer.Connect(); await buyer.Join(id);
        await Denied("400", () => buyer.Send(id, new(Guid.NewGuid(), new string('x', 4001))));
        await Denied("400", () => buyer.Send(id, new(Guid.Empty, "Private content key")));
        await Denied("400", () => buyer.Hub!.InvokeAsync<ChatReadReceipt>("MarkRead", id, "01"));
        var binding = await Assert.ThrowsAsync<HubException>(() => buyer.Hub!.InvokeAsync<ChatSendReceipt>("SendMessage", id, new { clientMessageId = "malformed-private-guid", content = "Private content binding" }));
        Assert.DoesNotContain("Private content", binding.Message); Assert.DoesNotContain("malformed-private-guid", binding.Message);
        await Assert.ThrowsAnyAsync<Exception>(() => buyer.Send(id, new(Guid.NewGuid(), new string('x', ChatLimits.MaxBodyBytes + 1))));
        await buyer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("0|0", SqlFixture.Scalar(f.Target, $"SELECT CONCAT(LastSequence,'|',(SELECT COUNT(*) FROM dbo.Message)) FROM dbo.ChatConversation WHERE Id={id}"));
    }

    [Fact]
    public async Task Subscription_limit_and_leave_are_server_owned_and_do_not_grant_foreign_access()
    {
        var f = await ChatFixture.Create(); await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider());
        var buyer = await host.Login(0); await buyer.Connect(); var ids = new List<int>();
        await using (var db = f.Open())
        {
            for (var i = 0; i < 51; i++)
            {
                var seller = new User { Role = "Seller", Password = "isolated-active-credential", Email = $"CAP{i}@EXAMPLE.INVALID" };
                db.Add(seller); await db.SaveChangesAsync();
                var c = new ChatConversation { BuyerId = f.Buyer, SellerId = seller.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
                db.Add(c); await db.SaveChangesAsync(); ids.Add(c.Id);
            }
        }
        foreach (var id in ids.Take(50)) await buyer.Join(id);
        await Denied("400", () => buyer.Join(ids[50]));
        await buyer.Hub!.InvokeAsync("LeaveConversation", ids[0]);
        Assert.Equal(ids[50], (await buyer.Join(ids[50])).Id);
    }

    [Fact]
    public async Task Unavailable_Redis_does_not_roll_back_committed_REST_receipt_and_SQL_catchup()
    {
        var f = await ChatFixture.Create(); var keys = new EphemeralDataProtectionProvider();
        await using var healthy = await ChatSocketHost.Start(f, keys);
        var portReservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); portReservation.Start();
        var unusedPort = ((IPEndPoint)portReservation.LocalEndpoint).Port; portReservation.Stop();
        await using var unavailable = await ChatSocketHost.Start(f, keys, redis: $"127.0.0.1:{unusedPort},abortConnect=false,connectRetry=0,connectTimeout=250,asyncTimeout=250");
        var buyer = await unavailable.Login(0); var seller = await healthy.Login(2); var id = await buyer.Open(f.Product);
        await seller.Connect(); await seller.Join(id);
        var command = new ChatSendCommand(Guid.NewGuid(), "durable despite unavailable Redis");
        var response = await buyer.Http.PostAsJsonAsync($"/api/v1/chat/conversations/{id}/messages", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<ChatSendReceipt>())!; Assert.False(receipt.Replayed);
        await seller.AssertQuiet();
        var page = (await seller.Http.GetFromJsonAsync<ChatHistoryPage>($"/api/v1/chat/conversations/{id}/messages?after=0"))!;
        Assert.Equal(receipt.Message, Assert.Single(page.Messages));
        var retry = await buyer.Http.PostAsJsonAsync($"/api/v1/chat/conversations/{id}/messages", command);
        Assert.True((await retry.Content.ReadFromJsonAsync<ChatSendReceipt>())!.Replayed);
        Assert.Equal("1|1", SqlFixture.Scalar(f.Target, $"SELECT CONCAT(LastSequence,'|',(SELECT COUNT(*) FROM dbo.Message)) FROM dbo.ChatConversation WHERE Id={id}"));
    }
}
