using EbayGroup5.Application.Chat;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace EbayGroup5.Web.Chat;

public sealed class ChatRealtime(IChatService chat, ChatDeliveryGuard guard, IHubContext<ChatHub> hub, ILogger<ChatRealtime> logger)
{
    public async Task<ChatSendReceipt> SendAsync(int actorId, int conversationId, ChatSendCommand command, CancellationToken ct)
    {
        var receipt = await chat.SendAsync(actorId, conversationId, command, ct);
        if (!receipt.Replayed) await Publish(conversationId, receipt.Message.Id, receipt.Message.Sequence, receipt.Message.SentAtUtc, receipt.Message.TraceId,
            (groups, token) => hub.Clients.Groups(groups).SendAsync(nameof(IChatClient.MessageCommitted), receipt.Message, token));
        return receipt;
    }
    public async Task<ChatReadReceipt> ReadAsync(int actorId, int conversationId, string throughSequence, CancellationToken ct)
    {
        var receipt = await chat.MarkReadAsync(actorId, conversationId, throughSequence, ct);
        if (receipt.Advanced) await Publish(conversationId, null, receipt.ThroughSequence, DateTimeOffset.UtcNow, null,
            (groups, token) => hub.Clients.Groups(groups).SendAsync(nameof(IChatClient.ReadAdvanced), receipt, token));
        return receipt;
    }
    private async Task Publish(int conversationId, int? messageId, string sequence, DateTimeOffset utc, string? traceId,
        Func<IReadOnlyList<string>, CancellationToken, Task> publish)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try {
            await guard.PublishAsync(conversationId, groups => publish(groups, timeout.Token).WaitAsync(timeout.Token), timeout.Token);
            logger.LogInformation("Chat live publish conversation={ConversationId} message={MessageId} sequence={Sequence} utc={Utc} trace={TraceId} requestTrace={RequestTraceId} result=enqueued",
                conversationId, messageId, sequence, utc, traceId, Activity.Current?.TraceId.ToString());
        }
        catch
        {
            // SQL is already committed: retain its receipt, never repersist or leak Redis exception/configuration.
            logger.LogWarning("Chat live publish conversation={ConversationId} message={MessageId} sequence={Sequence} utc={Utc} trace={TraceId} requestTrace={RequestTraceId} result=catch-up-required",
                conversationId, messageId, sequence, utc, traceId, Activity.Current?.TraceId.ToString());
        }
    }
}
