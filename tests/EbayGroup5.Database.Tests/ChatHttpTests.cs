using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class ChatHttpTests
{
    private const string Root = "/api/v1/chat";
    private static async Task<HttpClient> SignIn(AccountHttpTests.Factory factory, int index)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = $"chat{index}@example.invalid", ["Password"] = ChatFixture.Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var response = await client.GetAsync(Root + "/antiforgery");
        AssertNoStore(response); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Token>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", payload!.RequestToken);
        return client;
    }
    private sealed record Token(string RequestToken);
    private static void AssertNoStore(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore == true);
    private static async Task<ChatOpenResult> Open(HttpClient client, int product)
    {
        var response = await client.PostAsJsonAsync(Root + "/conversations", new { productId = product });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertNoStore(response);
        return (await response.Content.ReadFromJsonAsync<ChatOpenResult>())!;
    }
    private static async Task Check(HttpStatusCode status, Task<HttpResponseMessage> action)
    {
        using var response = await action; Assert.Equal(status, response.StatusCode); AssertNoStore(response);
        if (status == HttpStatusCode.Unauthorized) Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Unauthenticated_API_is_401_with_no_redirect_or_cache_for_all_operations()
    {
        var f = await ChatFixture.Create(); using var factory = new AccountHttpTests.Factory(f.Target);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "/antiforgery", "/conversations", "/conversations/1/messages" })
            await Check(HttpStatusCode.Unauthorized, client.GetAsync(Root + path));
        foreach (var path in new[] { "/conversations", "/conversations/1/messages", "/conversations/1/read" })
            await Check(HttpStatusCode.Unauthorized, client.PostAsJsonAsync(Root + path, new { }));
    }

    [Fact]
    public async Task HTTP_principal_owns_objects_and_ignores_sender_receiver_price_and_reader_overposting()
    {
        var f = await ChatFixture.Create(); using var factory = new AccountHttpTests.Factory(f.Target);
        using var buyer = await SignIn(factory, 0); using var other = await SignIn(factory, 1);
        using var seller = await SignIn(factory, 2); using var admin = await SignIn(factory, 4);
        var opened = await Open(buyer, f.Product); var path = Root + $"/conversations/{opened.Conversation.Id}";
        var key = Guid.NewGuid();
        var send = await buyer.PostAsJsonAsync(path + "/messages", new { clientMessageId = key, content = "HTTP chat", productId = f.Product,
            senderId = f.OtherBuyer, actorId = f.Admin, receiverId = f.OtherSeller, price = .01m, sequence = "999", traceId = "client" });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode); AssertNoStore(send);
        var receipt = (await send.Content.ReadFromJsonAsync<ChatSendReceipt>())!;
        Assert.Equal(f.Buyer, receipt.Message.SenderId); Assert.Equal(f.Seller, receipt.Message.ReceiverId);
        Assert.Equal(12.34m, receipt.Message.Context!.UnitPrice); Assert.Equal("1", receipt.Message.Sequence); Assert.False(receipt.Replayed);
        Assert.NotEqual("client", receipt.Message.TraceId);
        var replay = await buyer.PostAsJsonAsync(path + "/messages", new ChatSendCommand(key, "HTTP chat", f.Product));
        Assert.True((await replay.Content.ReadFromJsonAsync<ChatSendReceipt>())!.Replayed);
        await Check(HttpStatusCode.Conflict, buyer.PostAsJsonAsync(path + "/messages", new ChatSendCommand(key, "HTTP changed", f.Product)));
        var history = await seller.GetAsync(path + "/messages"); Assert.Equal(HttpStatusCode.OK, history.StatusCode); AssertNoStore(history);
        var body = await history.Content.ReadAsStringAsync(); Assert.Contains("HTTP chat", body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
        foreach (var denied in new[] { other, admin })
        {
            await Check(HttpStatusCode.NotFound, denied.GetAsync(path + "/messages"));
            await Check(HttpStatusCode.NotFound, denied.PostAsJsonAsync(path + "/messages", new ChatSendCommand(Guid.NewGuid(), "foreign")));
            await Check(HttpStatusCode.NotFound, denied.PostAsJsonAsync(path + "/read", new { throughSequence = "1" }));
        }
        await Check(HttpStatusCode.NotFound, buyer.GetAsync(Root + "/conversations/2147483647/messages"));
        await Check(HttpStatusCode.Forbidden, seller.PostAsJsonAsync(Root + "/conversations", new { productId = f.Product }));
        await Check(HttpStatusCode.Forbidden, admin.GetAsync(Root + "/conversations"));
        var read = await buyer.PostAsJsonAsync(path + "/read", new { throughSequence = "1", userId = f.Seller, readerId = f.Seller });
        Assert.Equal(new ChatReadReceipt(opened.Conversation.Id, f.Buyer, "1"), await read.Content.ReadFromJsonAsync<ChatReadReceipt>());
        var sellerInbox = (await seller.GetFromJsonAsync<ChatInboxPage>(Root + "/conversations"))!;
        var conversation = Assert.Single(sellerInbox.Conversations); Assert.Equal("0", conversation.OwnReadSequence); Assert.Equal("1", conversation.PeerReadSequence);
        await using var db = f.Open(); Assert.Equal(1, await db.Set<Message>().CountAsync());
    }

    [Fact]
    public async Task CSRF_is_required_for_every_unsafe_route_and_token_is_bound_to_current_user()
    {
        var f = await ChatFixture.Create(); using var factory = new AccountHttpTests.Factory(f.Target);
        using var buyer = await SignIn(factory, 0); using var other = await SignIn(factory, 1);
        var opened = await Open(buyer, f.Product); var path = Root + $"/conversations/{opened.Conversation.Id}";
        var goodToken = buyer.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        var otherToken = other.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        foreach (var invalid in new string?[] { null, "invalid", otherToken })
        {
            buyer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            if (invalid != null) buyer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", invalid);
            await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(Root + "/conversations", new { productId = f.Product }));
            await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/messages", new ChatSendCommand(Guid.NewGuid(), "csrf")));
            await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/read", new { throughSequence = "0" }));
        }
        buyer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); buyer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", goodToken);
        await Check(HttpStatusCode.OK, buyer.PostAsJsonAsync(path + "/messages", new ChatSendCommand(Guid.NewGuid(), "valid csrf")));
        await using var db = f.Open(); Assert.Equal(1, await db.Set<Message>().CountAsync());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("password")]
    public async Task Stale_cookie_API_is_revalidated_before_read_or_write(string change)
    {
        var f = await ChatFixture.Create(); using var factory = new AccountHttpTests.Factory(f.Target); using var buyer = await SignIn(factory, 0);
        var id = (await Open(buyer, f.Product)).Conversation.Id;
        await using var db = f.Open();
        if (change == "role") await db.Set<User>().Where(x => x.Id == f.Buyer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Seller"));
        else await db.Set<User>().Where(x => x.Id == f.Buyer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, (string?)null));
        await Check(HttpStatusCode.Unauthorized, buyer.GetAsync(Root + $"/conversations/{id}/messages"));
        await Check(HttpStatusCode.Unauthorized, buyer.PostAsJsonAsync(Root + $"/conversations/{id}/messages", new ChatSendCommand(Guid.NewGuid(), "stale")));
        Assert.Equal(0, await db.Set<Message>().CountAsync());
    }

    [Fact]
    public async Task HTTP_rejects_invalid_context_text_cursors_JSON_and_actual_oversize_body()
    {
        var f = await ChatFixture.Create(); using var factory = new AccountHttpTests.Factory(f.Target); using var buyer = await SignIn(factory, 0);
        var id = (await Open(buyer, f.Product)).Conversation.Id; var path = Root + $"/conversations/{id}";
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(Root + "/conversations", new { }));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(Root + "/conversations", new { productId = f.Product, orderItemId = f.Item }));
        await Check(HttpStatusCode.NotFound, buyer.PostAsJsonAsync(Root + "/conversations", new { orderItemId = f.ForeignItem }));
        var order = await buyer.PostAsJsonAsync(Root + "/conversations", new { orderItemId = f.Item });
        Assert.Equal(9.99m, (await order.Content.ReadFromJsonAsync<ChatOpenResult>())!.RequestedContext.UnitPrice);
        foreach (var query in new[] { "?before=0", "?before=1&after=0", "?after=01", "?after=1", "?limit=101", "?limit=invalid" })
            await Check(HttpStatusCode.BadRequest, buyer.GetAsync(path + "/messages" + query));
        await Check(HttpStatusCode.BadRequest, buyer.GetAsync(Root + "/conversations?cursorId=1"));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/read", new { throughSequence = 0 }));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/messages", new { clientMessageId = "bad-guid", content = "text" }));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/messages", new ChatSendCommand(Guid.Empty, "text")));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/messages", new ChatSendCommand(Guid.NewGuid(), new string('x', 4001))));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsJsonAsync(path + "/messages", new { clientMessageId = Guid.NewGuid(), content = (string?)null }));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsync(path + "/messages", new StringContent("{", Encoding.UTF8, "application/json")));
        var oversized = "{\"clientMessageId\":\"" + Guid.NewGuid() + "\",\"content\":\"fine\",\"ignored\":\"" + new string('x', ChatLimits.MaxBodyBytes) + "\"}";
        await Check(HttpStatusCode.BadRequest, buyer.PostAsync(path + "/messages", new StringContent(oversized, Encoding.UTF8, "application/json")));
        await Check(HttpStatusCode.BadRequest, buyer.PostAsync(path + "/messages", new UnknownLengthContent(oversized)));
        await using var db = f.Open(); Assert.Equal(0, await db.Set<Message>().CountAsync());
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthContent(string value)
        {
            bytes = Encoding.UTF8.GetBytes(value);
            Headers.ContentType = new("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    [Fact]
    public async Task Unexpected_service_error_is_generic_no_store_and_does_not_disclose_private_details()
    {
        var f = await ChatFixture.Create();
        using var factory = new AccountHttpTests.Factory(f.Target).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<IChatService>(); services.AddScoped<IChatService, FailingService>(); }));
        // SignIn helper takes the same production factory type; use a normal client login first then token on this host.
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = "chat0@example.invalid", ["Password"] = ChatFixture.Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var response = await client.GetAsync(Root + "/conversations");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); AssertNoStore(response);
        var body = await response.Content.ReadAsStringAsync(); Assert.Contains("An unexpected error occurred.", body); Assert.DoesNotContain("Private sentinel", body);
    }
    private sealed class FailingService : IChatService
    {
        public Task<ChatInboxPage> InboxAsync(int actorId, ChatInboxCursor? cursor = null, int limit = 20, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Private sentinel SQL credential message");
        public Task<ChatOpenResult> OpenForProductAsync(int actorId, int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatOpenResult> OpenForOrderItemAsync(int actorId, int orderItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatHistoryPage> HistoryAsync(int actorId, int conversationId, ChatHistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatSendReceipt> SendAsync(int actorId, int conversationId, ChatSendCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatReadReceipt> MarkReadAsync(int actorId, int conversationId, string throughSequence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
