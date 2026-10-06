using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Chat;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EbayGroup5.Database.Tests;

internal sealed record ChatFixture(string Target, int Buyer, int OtherBuyer, int Seller, int OtherSeller, int Admin, int Product, int SameSellerProduct, int OtherProduct, int Item, int ForeignItem)
{
    public const string Password = "Chat-Test-Password-789!";
    public EbayDbContext Open(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(Target).AddInterceptors(interceptors).Options);
    public static ChatService Service(EbayDbContext db, ILogger<ChatService>? logger = null) => new(db, logger ?? NullLogger<ChatService>.Instance);
    public static async Task<ChatFixture> Create()
    {
        var target = SqlFixture.NewTarget(); MigrationRunner.Run(target);
        await using var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
        var hash = AccountService.HashPassword(Password);
        var users = new[] { "Buyer", "Buyer", "Seller", "Seller", "Admin" }.Select((role, i) => new User { Role = role, Username = "Chat user " + i, Email = $"CHAT{i}@EXAMPLE.INVALID", Password = hash }).ToArray();
        db.AddRange(users); await db.SaveChangesAsync();
        var products = new[] {
            new Product { Title = "First chat listing", SellerId = users[2].Id, Price = 12.34m, IsAuction = false },
            new Product { Title = "Same seller listing", SellerId = users[2].Id, Price = 20m, IsAuction = false },
            new Product { Title = "Other seller listing", SellerId = users[3].Id, Price = 40m, IsAuction = false } };
        db.AddRange(products); await db.SaveChangesAsync();
        var orders = new[] { new OrderTable { BuyerId = users[0].Id, SellerId = users[2].Id, Status = "Delivered" }, new OrderTable { BuyerId = users[1].Id, SellerId = users[2].Id, Status = "Pending" } };
        db.AddRange(orders); await db.SaveChangesAsync();
        var items = orders.Select(o => new OrderItem { OrderId = o.Id, ProductId = products[0].Id, Quantity = 1, UnitPrice = 9.99m }).ToArray();
        db.AddRange(items); await db.SaveChangesAsync();
        return new(target, users[0].Id, users[1].Id, users[2].Id, users[3].Id, users[4].Id, products[0].Id, products[1].Id, products[2].Id, items[0].Id, items[1].Id);
    }
}

public sealed class ChatServiceTests
{
    private static async Task Failure(ChatError error, Func<Task> action) => Assert.Equal(error, (await Assert.ThrowsAsync<ChatException>(action)).Error);

    [Fact]
    public async Task Concurrent_open_reuses_pair_without_overwriting_initial_context()
    {
        var f = await ChatFixture.Create();
        async Task<ChatOpenResult> Open(int product) { await using var db = f.Open(); return await ChatFixture.Service(db).OpenForProductAsync(f.Buyer, product); }
        var opened = await Task.WhenAll(Open(f.Product), Open(f.SameSellerProduct));
        Assert.Equal(opened[0].Conversation.Id, opened[1].Conversation.Id);
        Assert.Equal(opened[0].Conversation.InitialContext, opened[1].Conversation.InitialContext);
        Assert.NotEqual(opened[0].RequestedContext, opened[1].RequestedContext);
        await using var db = f.Open(); var service = ChatFixture.Service(db);
        Assert.Equal(1, await db.Set<ChatConversation>().CountAsync());
        Assert.Equal(0, await db.Set<Message>().CountAsync());
        Assert.NotEqual(opened[0].Conversation.Id, (await service.OpenForProductAsync(f.Buyer, f.OtherProduct)).Conversation.Id);
        await Failure(ChatError.Forbidden, () => service.OpenForProductAsync(f.Seller, f.Product));
        await Failure(ChatError.Forbidden, () => service.OpenForProductAsync(f.Admin, f.Product));
    }

    [Fact]
    public async Task Order_context_uses_owned_order_seller_and_unit_price_after_listing_reassignment()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        await db.Set<Product>().Where(x => x.Id == f.Product).ExecuteUpdateAsync(x => x.SetProperty(p => p.SellerId, f.OtherSeller).SetProperty(p => p.Price, 100m));
        var opened = await service.OpenForOrderItemAsync(f.Buyer, f.Item);
        Assert.Equal(f.Seller, opened.Conversation.SellerId); Assert.Equal(9.99m, opened.RequestedContext.UnitPrice);
        var sent = await service.SendAsync(f.Seller, opened.Conversation.Id, new(Guid.NewGuid(), "Order reply", OrderItemId: f.Item));
        Assert.Equal(f.Buyer, sent.Message.ReceiverId); Assert.Equal(9.99m, sent.Message.Context!.UnitPrice);
        await Failure(ChatError.NotFound, () => service.OpenForOrderItemAsync(f.Buyer, f.ForeignItem));
        await Failure(ChatError.NotFound, () => service.SendAsync(f.Buyer, opened.Conversation.Id, new(Guid.NewGuid(), "Foreign order", OrderItemId: f.ForeignItem)));
        await Failure(ChatError.Invalid, () => service.SendAsync(f.Buyer, opened.Conversation.Id, new(Guid.NewGuid(), "Wrong product", f.OtherProduct, f.Item)));
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, opened.Conversation.Id, new(Guid.NewGuid(), "Wrong seller listing", f.Product)));
        Assert.Equal(1, await db.Set<Message>().CountAsync());
    }

    [Fact]
    public async Task Service_denies_foreign_missing_admin_and_changed_role_or_credentials_on_every_operation()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        foreach (var actor in new[] { f.OtherBuyer, f.OtherSeller, f.Admin, int.MaxValue })
        {
            await Failure(ChatError.NotFound, () => service.HistoryAsync(actor, id, new()));
            await Failure(ChatError.NotFound, () => service.SendAsync(actor, id, new(Guid.NewGuid(), "forbidden")));
            await Failure(ChatError.NotFound, () => service.MarkReadAsync(actor, id, "0"));
        }
        await Failure(ChatError.NotFound, () => service.HistoryAsync(f.Buyer, int.MaxValue, new()));
        Assert.Empty((await service.InboxAsync(f.OtherBuyer)).Conversations);
        await db.Set<User>().Where(x => x.Id == f.Buyer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Seller"));
        await Failure(ChatError.NotFound, () => service.HistoryAsync(f.Buyer, id, new()));
        await Failure(ChatError.NotFound, () => service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), "changed role")));
        await Failure(ChatError.NotFound, () => service.MarkReadAsync(f.Buyer, id, "0"));
        Assert.Empty((await service.InboxAsync(f.Buyer)).Conversations);
        await db.Set<User>().Where(x => x.Id == f.Buyer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Buyer").SetProperty(u => u.Password, (string?)null));
        await Failure(ChatError.NotFound, () => service.HistoryAsync(f.Buyer, id, new()));
        await Failure(ChatError.Forbidden, () => service.InboxAsync(f.Buyer));
        Assert.Equal(0, await db.Set<Message>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_service_serializes_concurrent_writes_and_retries(bool duplicate)
    {
        var f = await ChatFixture.Create(); int id;
        await using (var db = f.Open()) id = (await ChatFixture.Service(db).OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        var key = Guid.NewGuid();
        async Task<ChatSendReceipt> Send(Guid uuid) { await using var db = f.Open(); return await ChatFixture.Service(db).SendAsync(f.Buyer, id, new(uuid, "concurrent")); }
        var receipts = await Task.WhenAll(Send(key), Send(duplicate ? key : Guid.NewGuid()));
        Assert.Equal(duplicate ? new[] { "1", "1" } : new[] { "1", "2" }, receipts.Select(x => x.Message.Sequence).Order().ToArray());
        Assert.Equal(duplicate ? 1 : 0, receipts.Count(x => x.Replayed));
        await using var verify = f.Open();
        Assert.Equal(duplicate ? 1 : 2, await verify.Set<Message>().CountAsync());
        Assert.Equal(duplicate ? 1 : 2, (await verify.Set<ChatConversation>().SingleAsync()).LastSequence);
    }

    [Fact]
    public async Task Canonical_payload_replays_snapshot_after_mutations_and_rejects_changed_payload()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        var key = Guid.NewGuid();
        var sent = await service.SendAsync(f.Buyer, id, new(key, "\u2003 First\r\nline\rLast \u00a0", f.Product));
        Assert.Equal("First\nline\nLast", sent.Message.Content);
        Assert.Equal(TimeSpan.Zero, sent.Message.SentAtUtc.Offset);
        Assert.Matches("^[0-9a-f]{32}$", sent.Message.TraceId);
        await db.Set<Product>().Where(x => x.Id == f.Product).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 99m).SetProperty(p => p.IsAuction, true));
        await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, (string?)null));
        var replay = await service.SendAsync(f.Buyer, id, new(key, "First\nline\nLast", f.Product));
        Assert.True(replay.Replayed); Assert.Equal(sent.Message, replay.Message); Assert.Equal(12.34m, replay.Message.Context!.UnitPrice);
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, id, new(key, "different", f.Product)));
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, id, new(key, sent.Message.Content)));
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), "new message")));
        Assert.Single((await service.HistoryAsync(f.Buyer, id, new())).Messages);
        Assert.Equal("0", (await service.MarkReadAsync(f.Buyer, id, "0")).ThroughSequence);
        Assert.Equal(1, await db.Set<Message>().CountAsync());
    }

    [Fact]
    public async Task UUID_scope_separates_participants_and_conversations()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        var other = (await service.OpenForProductAsync(f.Buyer, f.OtherProduct)).Conversation.Id;
        var key = Guid.NewGuid();
        Assert.False((await service.SendAsync(f.Buyer, id, new(key, "buyer"))).Replayed);
        Assert.False((await service.SendAsync(f.Seller, id, new(key, "seller"))).Replayed);
        Assert.False((await service.SendAsync(f.Buyer, other, new(key, "other conversation"))).Replayed);
        Assert.Equal(new[] { f.Buyer, f.Seller }, (await service.HistoryAsync(f.Buyer, id, new())).Messages.Select(x => x.SenderId));
        Assert.Equal(3, await db.Set<Message>().CountAsync());
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Isolated failure before chat transaction commit.");
    }
    [Fact]
    public async Task Stale_listing_peer_role_and_sequence_exhaustion_fail_without_new_rows()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        await Failure(ChatError.NotFound, () => service.OpenForProductAsync(f.Buyer, int.MaxValue));
        await Failure(ChatError.Invalid, () => service.OpenForProductAsync(f.Buyer, 0));
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        var command = new ChatSendCommand(Guid.NewGuid(), "initial", f.Product);
        var original = await service.SendAsync(f.Buyer, id, command);
        await db.Set<Product>().Where(x => x.Id == f.Product).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsAuction, true));
        await Failure(ChatError.NotFound, () => service.OpenForProductAsync(f.Buyer, f.Product));
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), "stale listing", f.Product)));
        await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Buyer"));
        Assert.Equal(original.Message, (await service.SendAsync(f.Buyer, id, command)).Message);
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), "changed peer role")));
        await Failure(ChatError.NotFound, () => service.HistoryAsync(f.Seller, id, new()));
        Assert.Single((await service.HistoryAsync(f.Buyer, id, new())).Messages);
        await db.Set<User>().Where(x => x.Id == f.Seller).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Seller"));
        SqlFixture.Execute(f.Target, $"UPDATE dbo.ChatConversation SET LastSequence={long.MaxValue} WHERE Id={id}");
        await Failure(ChatError.Conflict, () => service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), "sequence exhausted")));
        Assert.True((await service.SendAsync(f.Buyer, id, command)).Replayed);
        Assert.Equal(1, await db.Set<Message>().CountAsync());
    }
    [Fact]
    public async Task Failure_after_SQL_writes_rolls_back_message_counter_and_UUID_for_retry()
    {
        var f = await ChatFixture.Create(); int id; var key = Guid.NewGuid();
        await using (var db = f.Open()) id = (await ChatFixture.Service(db).OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        await using (var failed = f.Open(new FailAfterSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => ChatFixture.Service(failed).SendAsync(f.Buyer, id, new(key, "retry after rollback")));
        await using var verify = f.Open(); var service = ChatFixture.Service(verify);
        Assert.Equal(0, await verify.Set<Message>().CountAsync());
        Assert.Equal(0, (await verify.Set<ChatConversation>().SingleAsync()).LastSequence);
        var receipt = await service.SendAsync(f.Buyer, id, new(key, "retry after rollback"));
        Assert.False(receipt.Replayed); Assert.Equal("1", receipt.Message.Sequence);
    }

    [Theory]
    [InlineData("whitespace")]
    [InlineData("long")]
    [InlineData("key")]
    [InlineData("surrogate")]
    [InlineData("null")]
    public async Task Invalid_plaintext_and_keys_leave_counter_unchanged(string invalid)
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        var text = invalid switch { "whitespace" => "\u2003\t\r\n\u00a0", "long" => new string('x', 4001), "surrogate" => "\ud800", "null" => null!, _ => "valid" };
        await Failure(ChatError.Invalid, () => service.SendAsync(f.Buyer, id, new(invalid == "key" ? Guid.Empty : Guid.NewGuid(), text)));
        Assert.Equal(0, await db.Set<Message>().CountAsync()); Assert.Equal(0, (await db.Set<ChatConversation>().SingleAsync()).LastSequence);
        var valid = string.Concat(Enumerable.Repeat("😀", 2000));
        Assert.Equal(valid, (await service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), valid))).Message.Content);
    }

    [Fact]
    public async Task History_is_bounded_exclusive_ordered_durable_and_excludes_legacy()
    {
        var f = await ChatFixture.Create(); int id;
        await using (var db = f.Open()) id = (await ChatFixture.Service(db).OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        SqlFixture.Execute(f.Target, $"""
            INSERT dbo.Message(senderId,receiverId,content) VALUES({f.Buyer},{f.Seller},N'legacy unlinked');
            BEGIN TRANSACTION;
            DECLARE @i int=1;
            WHILE @i<=125
            BEGIN
                INSERT dbo.Message(senderId,receiverId,content,[timestamp],ConversationId,ConversationBuyerId,ConversationSellerId,Sequence,ClientMessageId,PayloadHash,SentAtUtc,TraceId)
                VALUES({f.Buyer},{f.Seller},CONCAT(N'message ',@i),SYSUTCDATETIME(),{id},{f.Buyer},{f.Seller},@i,NEWID(),HASHBYTES('SHA2_256',CONCAT('message ',@i)),SYSUTCDATETIME(),'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
                SET @i+=1;
            END;
            UPDATE dbo.ChatConversation SET LastSequence=125 WHERE Id={id};
            COMMIT;
            """);
        await using var reopened = f.Open(); var service = ChatFixture.Service(reopened);
        var newest = await service.HistoryAsync(f.Seller, id, new());
        Assert.Equal(Enumerable.Range(76, 50).Select(x => x.ToString()), newest.Messages.Select(x => x.Sequence));
        Assert.Equal("125", newest.HighWatermark); Assert.True(newest.HasMore);
        var previous = await service.HistoryAsync(f.Buyer, id, new(Before: newest.OldestCursor));
        Assert.Equal(Enumerable.Range(26, 50).Select(x => x.ToString()), previous.Messages.Select(x => x.Sequence));
        var first = await service.HistoryAsync(f.Buyer, id, new(Before: previous.OldestCursor));
        Assert.Equal(25, first.Messages.Count); Assert.False(first.HasMore);
        var after = await service.HistoryAsync(f.Buyer, id, new(After: "0", Limit: 100));
        Assert.Equal("1", after.OldestCursor); Assert.Equal("100", after.NewestCursor); Assert.True(after.HasMore);
        Assert.Empty((await service.HistoryAsync(f.Buyer, id, new(After: "125"))).Messages);
        await service.SendAsync(f.Seller, id, new(Guid.NewGuid(), "next commit"));
        Assert.Equal("126", Assert.Single((await service.HistoryAsync(f.Buyer, id, new(After: "125"))).Messages).Sequence);
        foreach (var query in new[] { new ChatHistoryQuery(Before: "0"), new(After: "127"), new(Before: "2", After: "0"), new(After: "01"), new(Limit: 101), new(Limit: 0) })
            await Failure(ChatError.Invalid, () => service.HistoryAsync(f.Buyer, id, query));
    }

    [Fact]
    public async Task Own_read_is_monotonic_separate_from_peer_and_sending_does_not_mark_either_read()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        await service.SendAsync(f.Buyer, id, new(Guid.NewGuid(), "one"));
        await service.SendAsync(f.Seller, id, new(Guid.NewGuid(), "two"));
        var buyer = Assert.Single((await service.InboxAsync(f.Buyer)).Conversations);
        Assert.Equal("0", buyer.OwnReadSequence); Assert.Equal("0", buyer.PeerReadSequence); Assert.Equal("1", buyer.UnreadCount);
        Assert.Equal(new ChatReadReceipt(id, f.Buyer, "2"), await service.MarkReadAsync(f.Buyer, id, "2"));
        Assert.Equal("2", (await service.MarkReadAsync(f.Buyer, id, "1")).ThroughSequence);
        var seller = Assert.Single((await service.InboxAsync(f.Seller)).Conversations);
        Assert.Equal("0", seller.OwnReadSequence); Assert.Equal("2", seller.PeerReadSequence); Assert.Equal("1", seller.UnreadCount);
        Assert.Equal("0", Assert.Single((await service.InboxAsync(f.Buyer)).Conversations).UnreadCount);
        await Failure(ChatError.Invalid, () => service.MarkReadAsync(f.Buyer, id, "3"));
        await Failure(ChatError.Invalid, () => service.MarkReadAsync(f.Buyer, id, "-1"));
        Assert.Equal("0", (await service.MarkReadAsync(f.Seller, id, "0")).ThroughSequence);
    }

    [Fact]
    public async Task Inbox_keyset_is_owned_bounded_and_uses_id_to_break_identical_UTC_times()
    {
        var f = await ChatFixture.Create(); await using var db = f.Open(); var service = ChatFixture.Service(db);
        var firstId = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        var secondId = (await service.OpenForProductAsync(f.Buyer, f.OtherProduct)).Conversation.Id;
        await service.OpenForProductAsync(f.OtherBuyer, f.Product);
        SqlFixture.Execute(f.Target, "UPDATE dbo.ChatConversation SET UpdatedAtUtc='20261006'");
        var first = await service.InboxAsync(f.Buyer, limit: 1);
        Assert.Equal(secondId, Assert.Single(first.Conversations).Id); Assert.NotNull(first.NextCursor);
        var next = await service.InboxAsync(f.Buyer, first.NextCursor, 1);
        Assert.Equal(firstId, Assert.Single(next.Conversations).Id); Assert.Null(next.NextCursor);
        await Failure(ChatError.Invalid, () => service.InboxAsync(f.Buyer, limit: 51));
        await Failure(ChatError.Invalid, () => service.InboxAsync(f.Buyer, new(DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(7)), 1)));
    }

    [Fact]
    public async Task Runtime_SQL_login_executes_service_and_keeps_bigint_cursors_and_metadata_only_logs()
    {
        var f = await ChatFixture.Create(); var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(f.Target, login, password);
        var appTarget = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(f.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        var logs = new ReceiptLogger();
        await using var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(appTarget).Options);
        var service = ChatFixture.Service(db, logs);
        var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id;
        SqlFixture.Execute(f.Target, $"UPDATE dbo.ChatConversation SET LastSequence=9007199254740992 WHERE Id={id}");
        var command = new ChatSendCommand(Guid.NewGuid(), "Private content sentinel");
        var receipt = await service.SendAsync(f.Buyer, id, command);
        Assert.Equal("9007199254740993", receipt.Message.Sequence);
        Assert.Equal(receipt.Message, (await service.SendAsync(f.Buyer, id, command)).Message);
        Assert.Equal(receipt.Message, Assert.Single((await service.HistoryAsync(f.Seller, id, new(After: "9007199254740992"))).Messages));
        Assert.Equal("9007199254740993", (await service.MarkReadAsync(f.Seller, id, receipt.Message.Sequence)).ThroughSequence);
        Assert.Equal(2, logs.Lines.Count); Assert.All(logs.Lines, line => { Assert.Contains(receipt.Message.TraceId, line); Assert.DoesNotContain(command.Content, line); Assert.DoesNotContain(password, line); });
    }
    private sealed class ReceiptLogger : ILogger<ChatService>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
