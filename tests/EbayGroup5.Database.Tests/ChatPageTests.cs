using System.Net;
using System.Net.Http.Json;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class ChatPageTests
{
    [Fact]
    public async Task Messages_pages_require_role_and_owned_conversation_without_GET_side_effects()
    {
        var f = await ChatFixture.Create();
        using var factory = new AccountHttpTests.Factory(f.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Messages")).StatusCode);
        // Real cookie login/CSRF through the existing production test host.
        using var buyer = await ChatHttpTests.SignIn(factory, 0); using var other = await ChatHttpTests.SignIn(factory, 1);
        using var admin = await ChatHttpTests.SignIn(factory, 4); using var seller = await ChatHttpTests.SignIn(factory, 2);
        var empty = await buyer.GetAsync("/Messages"); Assert.Equal(HttpStatusCode.OK, empty.StatusCode); Assert.True(empty.Headers.CacheControl!.NoStore);
        await using var db = f.Open(); Assert.Equal(0, await db.Set<ChatConversation>().CountAsync());
        var opened = await ChatFixture.Service(db).OpenForProductAsync(f.Buyer, f.Product);
        var url = "/Messages?conversationId=" + opened.Conversation.Id;
        Assert.Equal(HttpStatusCode.OK, (await seller.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync("/Messages?conversationId=2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/Messages?conversationId=bad")).StatusCode);
        Assert.Equal(0, await db.Set<Message>().CountAsync());
        var html = await buyer.GetStringAsync(url);
        Assert.Contains("data-chat-root", html); Assert.Contains("Load older messages", html); Assert.Contains("Load more conversations", html);
        Assert.Contains("/lib/signalr/signalr.min.js", html); Assert.Contains("/js/chat.js", html);
        Assert.DoesNotContain("password", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contact_entry_points_and_My_Ebay_navigation_are_role_correct()
    {
        var f = await ChatFixture.Create(); using var factory = new AccountHttpTests.Factory(f.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        var anon = await anonymous.GetStringAsync("/Catalog/Details/" + f.Product);
        Assert.Contains("seller-message", anon); Assert.Contains("returnUrl=", anon, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("id=\"chat-panel\"", anon);
        using var buyer = await ChatHttpTests.SignIn(factory, 0); using var seller = await ChatHttpTests.SignIn(factory, 2);
        var buyerPage = await buyer.GetStringAsync("/Catalog/Details/" + f.Product);
        Assert.Contains("data-chat-product-id=\"" + f.Product, buyerPage); Assert.Contains("id=\"chat-panel\"", buyerPage); Assert.Contains(">Messages</a>", buyerPage);
        Assert.DoesNotContain("Messaging is not available yet", buyerPage);
        var sellerPage = await seller.GetStringAsync("/Catalog/Details/" + f.Product);
        Assert.DoesNotContain("data-chat-product-id", sellerPage); Assert.Contains(">Messages</a>", sellerPage);
        await using var db = f.Open(); var orderId = (await db.Set<OrderItem>().SingleAsync(x => x.Id == f.Item)).OrderId;
        var order = await buyer.GetStringAsync("/Orders/Details/" + orderId);
        Assert.Contains("data-chat-order-item-id=\"" + f.Item, order);
        Assert.Equal(0, await db.Set<ChatConversation>().CountAsync());
    }

    [Fact]
    public async Task Owned_summary_exposes_bounded_plaintext_public_peer_name_and_precise_snapshot_only()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Username, "<img src=x onerror=alert(1)> Seller"));
        var opened = await service.OpenForOrderItemAsync(f.Buyer, f.Item);
        var text = new string('a', 119) + "😀" + " trailing private body";
        await service.SendAsync(f.Seller, opened.Conversation.Id, new(Guid.NewGuid(), text));
        using var factory = new AccountHttpTests.Factory(f.Target); using var buyer = await ChatHttpTests.SignIn(factory, 0); using var other = await ChatHttpTests.SignIn(factory, 1);
        var response = await buyer.GetAsync("/api/v1/chat/conversations/" + opened.Conversation.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var dto = (await response.Content.ReadFromJsonAsync<ChatConversationDto>())!;
        Assert.Equal("<img src=x onerror=alert(1)> Seller", dto.PeerName); Assert.Equal(new string('a', 119), dto.LastMessagePreview);
        Assert.Equal(TimeSpan.Zero, dto.LastMessageAtUtc!.Value.Offset); Assert.Equal("1", dto.UnreadCount); Assert.Equal(9.99m, dto.InitialContext!.UnitPrice);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/v1/chat/conversations/" + opened.Conversation.Id)).StatusCode);
    }
}
