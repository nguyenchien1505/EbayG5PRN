using System.Globalization;

namespace EbayGroup5.Application.Chat;

public static class ChatLimits
{
    public const int MaxTextLength = 4000;
    public const int DefaultHistorySize = 50;
    public const int MaxHistorySize = 100;
    public const int DefaultInboxSize = 20;
    public const int MaxInboxSize = 50;
    public const int MaxBodyBytes = 32768;
}

public static class ChatCursor
{
    public static string Format(long sequence) => sequence >= 0 ? sequence.ToString(CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException(nameof(sequence));
    public static bool TryParse(string? text, out long sequence)
    {
        sequence = 0;
        return text != null && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence >= 0 && Format(sequence) == text;
    }
}

public sealed record ChatContext(int ProductId, int? OrderItemId, string Title, decimal UnitPrice);
public sealed record ChatConversationDto(int Id, int BuyerId, int SellerId, string LastSequence, string OwnReadSequence,
    DateTimeOffset UpdatedAtUtc, ChatContext? InitialContext);
public sealed record ChatOpenResult(ChatConversationDto Conversation, ChatContext RequestedContext);
public sealed record ChatInboxCursor(DateTimeOffset UpdatedAtUtc, int ConversationId);
public sealed record ChatInboxPage(IReadOnlyList<ChatConversationDto> Conversations, ChatInboxCursor? NextCursor);
public sealed record ChatHistoryQuery(string? Before = null, string? After = null, int Limit = ChatLimits.DefaultHistorySize);
public sealed record ChatSendCommand(Guid ClientMessageId, string Content, int? ProductId = null, int? OrderItemId = null);
public sealed record ChatMessageDto(int Id, int ConversationId, string Sequence, Guid ClientMessageId, int SenderId, int ReceiverId,
    string Content, DateTimeOffset SentAtUtc, string TraceId, ChatContext? Context);
public sealed record ChatHistoryPage(IReadOnlyList<ChatMessageDto> Messages, string? OldestCursor, string? NewestCursor, bool HasMore, string HighWatermark);
public sealed record ChatSendReceipt(ChatMessageDto Message, bool Replayed);
public sealed record ChatReadReceipt(int ConversationId, string ThroughSequence);

// actorId is supplied by the authenticated Web adapter, never bound from client request data.
// C02 implements authorization, normalization and persistence; C01 provides no runtime endpoints.
public interface IChatService
{
    Task<ChatOpenResult> OpenForProductAsync(int actorId, int productId, CancellationToken cancellationToken = default);
    Task<ChatOpenResult> OpenForOrderItemAsync(int actorId, int orderItemId, CancellationToken cancellationToken = default);
    Task<ChatInboxPage> InboxAsync(int actorId, ChatInboxCursor? cursor = null, int limit = ChatLimits.DefaultInboxSize, CancellationToken cancellationToken = default);
    Task<ChatHistoryPage> HistoryAsync(int actorId, int conversationId, ChatHistoryQuery query, CancellationToken cancellationToken = default);
    Task<ChatSendReceipt> SendAsync(int actorId, int conversationId, ChatSendCommand command, CancellationToken cancellationToken = default);
    Task<ChatReadReceipt> MarkReadAsync(int actorId, int conversationId, string throughSequence, CancellationToken cancellationToken = default);
}
