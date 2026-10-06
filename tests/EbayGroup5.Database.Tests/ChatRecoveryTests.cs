using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EbayGroup5.Application.Chat;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class ChatRecoveryTests
{
    [Fact]
    public async Task Restart_rejoins_new_groups_and_recovers_paged_SQL_history_and_read_cursors_without_duplicate_retry()
    {
        var f = await ChatFixture.Create(); var keys = new EphemeralDataProtectionProvider();
        await using var first = await ChatSocketHost.Start(f, keys);
        var buyer = await first.Login(0); var seller = await first.Login(2);
        var id = await buyer.Open(f.Product); await buyer.Connect(); await seller.Connect();
        await buyer.Join(id); await seller.Join(id);
        var acknowledged = await buyer.Send(id, new(Guid.NewGuid(), "confirmed before restart"));
        await buyer.NextMessage(); await seller.NextMessage();
        await seller.Http.PostAsJsonAsync($"/api/v1/chat/conversations/{id}/read", new { throughSequence = "1" });
        await first.Stop(); await buyer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Simulate the durable commit-before-event/ack crash window with the real SQL service.
        var uncertain = new ChatSendCommand(Guid.NewGuid(), "committed with missing event and receipt", f.Product);
        ChatSendReceipt lost;
        await using (var db = f.Open())
        {
            var service = ChatFixture.Service(db);
            lost = await service.SendAsync(f.Buyer, id, uncertain);
            for (var i = 0; i < 124; i++) await service.SendAsync(f.Seller, id, new(Guid.NewGuid(), "offline history " + i));
        }
        // Fresh host/DI root on the same address, shared test keys, unchanged database.
        await using var restarted = await ChatSocketHost.Start(f, keys, address: first.Address.ToString());
        await buyer.Hub!.StartAsync();
        var summary = await buyer.Join(id);
        Assert.Equal("126", summary.LastSequence); Assert.Equal("1", summary.PeerReadSequence);
        Assert.Equal("0", summary.OwnReadSequence); Assert.Equal("124", summary.UnreadCount);
        var page1 = (await buyer.Http.GetFromJsonAsync<ChatHistoryPage>($"/api/v1/chat/conversations/{id}/messages?after=1&limit=100"))!;
        Assert.True(page1.HasMore); Assert.Equal("101", page1.NewestCursor);
        var page2 = (await buyer.Http.GetFromJsonAsync<ChatHistoryPage>($"/api/v1/chat/conversations/{id}/messages?after=101&limit=100"))!;
        Assert.False(page2.HasMore);
        Assert.Equal(Enumerable.Range(2, 125).Select(x => x.ToString()), page1.Messages.Concat(page2.Messages).Select(x => x.Sequence));
        Assert.Equal(lost.Message, page1.Messages[0]); Assert.Equal("1", acknowledged.Message.Sequence);
        var retry = await buyer.Send(id, uncertain);
        Assert.True(retry.Replayed); Assert.Equal(lost.Message, retry.Message); await buyer.AssertQuiet();
        var freshSeller = await restarted.Login(2); await freshSeller.Connect(); await freshSeller.Join(id);
        var next = await freshSeller.Send(id, new(Guid.NewGuid(), "live after restart"));
        Assert.Equal(next.Message, await buyer.NextMessage()); Assert.Equal("127", next.Message.Sequence);
        Assert.Equal("127|127|1", SqlFixture.Scalar(f.Target,
            $"SELECT CONCAT(LastSequence,'|',(SELECT COUNT(*) FROM dbo.Message WHERE ConversationId={id}),'|',(SELECT COUNT(*) FROM dbo.Message WHERE ConversationId={id} AND ClientMessageId='{uncertain.ClientMessageId}')) FROM dbo.ChatConversation WHERE Id={id}"));
        var third = await restarted.Login(1);
        Assert.Equal(HttpStatusCode.NotFound, (await third.Http.GetAsync($"/api/v1/chat/conversations/{id}/messages?after=0")).StatusCode);
    }

    [Fact]
    public async Task Record_trace_survives_retry_while_new_request_trace_and_catchup_logs_exclude_content_and_credentials()
    {
        var f = await ChatFixture.Create(); var logs = new MetadataLogs();
        var command = new ChatSendCommand(Guid.NewGuid(), "PRIVATE-C05-CONTENT-SENTINEL");
        ChatSendReceipt first, retry;
        string originalTrace, replayTrace;
        await using var db = f.Open(); var service = ChatFixture.Service(db, logs.CreateLoggerForService());
        using (var activity = new Activity("original").SetIdFormat(ActivityIdFormat.W3C).Start())
        { originalTrace = activity.TraceId.ToString(); var id = (await service.OpenForProductAsync(f.Buyer, f.Product)).Conversation.Id; first = await service.SendAsync(f.Buyer, id, command); }
        using (var activity = new Activity("retry").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            replayTrace = activity.TraceId.ToString(); retry = await service.SendAsync(f.Buyer, first.Message.ConversationId, command);
            await service.HistoryAsync(f.Seller, first.Message.ConversationId, new(After: "0"));
        }
        Assert.True(retry.Replayed); Assert.Equal(first.Message, retry.Message);
        Assert.NotEqual(originalTrace, replayTrace); Assert.Equal(originalTrace, first.Message.TraceId);
        Assert.Contains(logs.Lines, x => x.Contains("replayed=True") && x.Contains("trace=" + originalTrace) && x.Contains("requestTrace=" + replayTrace));
        Assert.Contains(logs.Lines, x => x.Contains("Chat catch-up") && x.Contains("requestTrace=" + replayTrace) && x.Contains("watermark=1"));
        Assert.All(logs.Lines, x => { Assert.DoesNotContain(command.Content, x); Assert.DoesNotContain(ChatFixture.Password, x); });
    }

    [Fact]
    public async Task Failed_live_publish_log_correlates_the_persisted_receipt_without_raw_transport_error()
    {
        var f = await ChatFixture.Create(); var logs = new MetadataLogs();
        var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        await using var host = await ChatSocketHost.Start(f, new EphemeralDataProtectionProvider(),
            redis: $"127.0.0.1:{port},abortConnect=false,connectRetry=0,connectTimeout=250,asyncTimeout=250", logs: logs);
        var buyer = await host.Login(0); var id = await buyer.Open(f.Product);
        var command = new ChatSendCommand(Guid.NewGuid(), "PRIVATE-C05-PUBLISH-SENTINEL");
        var response = await buyer.Http.PostAsJsonAsync($"/api/v1/chat/conversations/{id}/messages", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<ChatSendReceipt>())!;
        var line = Assert.Single(logs.Lines, x => x.Contains("result=catch-up-required"));
        Assert.Contains("message=" + receipt.Message.Id, line); Assert.Contains("sequence=" + receipt.Message.Sequence, line);
        Assert.Contains("trace=" + receipt.Message.TraceId, line); Assert.Contains("utc=", line); Assert.Contains("requestTrace=", line);
        Assert.All(logs.Lines, x => { Assert.DoesNotContain(command.Content, x); Assert.DoesNotContain(ChatFixture.Password, x); Assert.DoesNotContain("127.0.0.1:", x); });
        Assert.Equal(receipt.Message, Assert.Single((await buyer.Http.GetFromJsonAsync<ChatHistoryPage>($"/api/v1/chat/conversations/{id}/messages?after=0"))!.Messages));
    }

    private sealed class MetadataLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Collector(Lines, categoryName.Contains(".Chat."));
        public ILogger<EbayGroup5.Infrastructure.Chat.ChatService> CreateLoggerForService() => new ServiceCollector(Lines);
        public void Dispose() { }
        private class Collector(ConcurrentQueue<string> lines, bool enabled) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => enabled;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (enabled) lines.Enqueue(formatter(state, exception)); }
        }
        private sealed class ServiceCollector(ConcurrentQueue<string> lines) : Collector(lines, true), ILogger<EbayGroup5.Infrastructure.Chat.ChatService> { }
    }
}
