using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EbayGroup5.Infrastructure.Chat;

public sealed class ChatService(EbayDbContext db, ILogger<ChatService> logger) : IChatService
{
    private static ChatException Error(ChatError error) => new(error);
    private static bool Active(User? user, string role) => user?.Role == role && !string.IsNullOrEmpty(user.Password);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static ChatContext? Context(int? product, int? item, string? title, decimal? price) =>
        product is int id ? new(id, item, title!, price!.Value) : null;
    private static ChatMessageDto MessageDto(Message m) => new(m.Id, m.ConversationId!.Value, ChatCursor.Format(m.Sequence!.Value),
        m.ClientMessageId!.Value, m.SenderId!.Value, m.ReceiverId!.Value, m.Content!, Utc(m.SentAtUtc!.Value), m.TraceId!,
        Context(m.ContextProductId, m.ContextOrderItemId, m.ContextTitle, m.ContextUnitPrice));
    private async Task<ChatConversationDto> ConversationDto(ChatConversation c, int actor, CancellationToken ct)
    {
        var ownRead = actor == c.BuyerId ? c.BuyerReadSequence : c.SellerReadSequence;
        var unread = await db.Set<Message>().LongCountAsync(x => x.ConversationId == c.Id && x.SenderId != actor && x.Sequence > ownRead, ct);
        var peerId = actor == c.BuyerId ? c.SellerId : c.BuyerId;
        var peerName = await db.Set<User>().Where(x => x.Id == peerId).Select(x => x.Username).SingleOrDefaultAsync(ct);
        var latest = await db.Set<Message>().AsNoTracking().Where(x => x.ConversationId == c.Id && x.Sequence <= c.LastSequence)
            .OrderByDescending(x => x.Sequence).Select(x => new { Preview = x.Content!.Substring(0, x.Content.Length > 120 ? 120 : x.Content.Length), x.SentAtUtc }).FirstOrDefaultAsync(ct);
        return ConversationDto(c, actor, unread, peerName, latest?.Preview, latest?.SentAtUtc);
    }
    private static ChatConversationDto ConversationDto(ChatConversation c, int actor, long unread, string? peerName, string? preview, DateTime? sent)
    {
        var ownRead = actor == c.BuyerId ? c.BuyerReadSequence : c.SellerReadSequence;
        var peerRead = actor == c.BuyerId ? c.SellerReadSequence : c.BuyerReadSequence;
        if (preview?.Length > 0 && char.IsHighSurrogate(preview[^1])) preview = preview[..^1];
        return new(c.Id, c.BuyerId, c.SellerId, ChatCursor.Format(c.LastSequence), ChatCursor.Format(ownRead), ChatCursor.Format(peerRead),
            Utc(c.UpdatedAtUtc), Context(c.ContextProductId, c.ContextOrderItemId, c.ContextTitle, c.ContextUnitPrice), ChatCursor.Format(unread),
            string.IsNullOrWhiteSpace(peerName) ? (actor == c.BuyerId ? "Seller" : "Buyer") : peerName, preview,
            sent is DateTime value ? Utc(value) : null);
    }
    private Task<User?> UserAsync(int id, CancellationToken ct) => db.Set<User>()
        .FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={id}").AsNoTracking().SingleOrDefaultAsync(ct);
    private async Task<User> Actor(int id, ChatError failure, CancellationToken ct)
    {
        var user = await UserAsync(id, ct);
        if (user?.Role is not ("Buyer" or "Seller") || string.IsNullOrEmpty(user.Password)) throw Error(failure);
        return user;
    }
    private async Task<ChatConversation> Owned(int actorId, int conversationId, bool update, CancellationToken ct)
    {
        // All mutators acquire the conversation lock before user/context locks.
        var rows = update
            ? db.Set<ChatConversation>().FromSqlInterpolated($"SELECT * FROM dbo.ChatConversation WITH (UPDLOCK,HOLDLOCK) WHERE Id={conversationId}")
            : db.Set<ChatConversation>().FromSqlInterpolated($"SELECT * FROM dbo.ChatConversation WITH (HOLDLOCK) WHERE Id={conversationId}");
        var c = await rows.AsNoTracking().SingleOrDefaultAsync(ct);
        if (c == null) throw Error(ChatError.NotFound);
        var actor = await Actor(actorId, ChatError.NotFound, ct);
        if (!(actor.Role == "Buyer" && c.BuyerId == actorId || actor.Role == "Seller" && c.SellerId == actorId)) throw Error(ChatError.NotFound);
        return c;
    }
    private async Task RequirePeer(ChatConversation c, int actor, CancellationToken ct)
    {
        var peer = await UserAsync(actor == c.BuyerId ? c.SellerId : c.BuyerId, ct);
        if (!Active(peer, actor == c.BuyerId ? "Seller" : "Buyer")) throw Error(ChatError.Conflict);
    }

    public async Task<ChatConversationDto> ConversationAsync(int actorId, int conversationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var c = await Owned(actorId, conversationId, false, cancellationToken);
        var dto = await ConversationDto(c, actorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return dto;
    }

    private sealed record Resolved(int SellerId, ChatContext Context);
    private async Task<Resolved> Resolve(int buyer, int? productId, int? orderItemId, CancellationToken ct, bool forSend = false)
    {
        if (productId is <= 0 || orderItemId is <= 0 || productId == null && orderItemId == null) throw Error(ChatError.Invalid);
        if (orderItemId is int itemId)
        {
            var item = await db.Set<OrderItem>().FromSqlInterpolated($"SELECT * FROM dbo.OrderItem WITH (HOLDLOCK) WHERE id={itemId}").AsNoTracking().SingleOrDefaultAsync(ct);
            if (item?.OrderId == null) throw Error(ChatError.NotFound);
            var order = await db.Set<OrderTable>().FromSqlInterpolated($"SELECT * FROM dbo.OrderTable WITH (HOLDLOCK) WHERE id={item.OrderId}").AsNoTracking().SingleOrDefaultAsync(ct);
            if (order?.BuyerId != buyer) throw Error(ChatError.NotFound);
            if (item.ProductId == null || order.SellerId == null || item.UnitPrice is not >= 0 || item.Quantity is not > 0) throw Error(ChatError.Conflict);
            if (productId != null && productId != item.ProductId) throw Error(ChatError.Invalid);
            var product = await db.Set<Product>().FromSqlInterpolated($"SELECT * FROM dbo.Product WITH (HOLDLOCK) WHERE id={item.ProductId}").AsNoTracking().SingleOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(product?.Title)) throw Error(ChatError.Conflict);
            return new(order.SellerId.Value, new(product.Id, item.Id, product.Title, item.UnitPrice.Value));
        }
        var listing = await db.Set<Product>().FromSqlInterpolated($"SELECT * FROM dbo.Product WITH (HOLDLOCK) WHERE id={productId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (listing == null) throw Error(ChatError.NotFound);
        if (listing.IsAuction != false || string.IsNullOrWhiteSpace(listing.Title) || listing.Price is not >= 0 || listing.SellerId == null)
            throw Error(forSend ? ChatError.Conflict : ChatError.NotFound);
        return new(listing.SellerId.Value, new(listing.Id, null, listing.Title, listing.Price.Value));
    }

    public Task<ChatOpenResult> OpenForProductAsync(int actorId, int productId, CancellationToken cancellationToken = default) => Open(actorId, productId, null, cancellationToken);
    public Task<ChatOpenResult> OpenForOrderItemAsync(int actorId, int orderItemId, CancellationToken cancellationToken = default) => Open(actorId, null, orderItemId, cancellationToken);
    private async Task<ChatOpenResult> Open(int actorId, int? productId, int? orderItemId, CancellationToken ct)
    {
        // Preliminary lookup determines the pair; all mutable data is checked again under locks.
        var actor = await Actor(actorId, ChatError.Forbidden, ct);
        if (actor.Role != "Buyer") throw Error(ChatError.Forbidden);
        var candidate = await Resolve(actorId, productId, orderItemId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var c = await db.Set<ChatConversation>().FromSqlInterpolated($"SELECT * FROM dbo.ChatConversation WITH (UPDLOCK,HOLDLOCK,INDEX(UQ_ChatConversation_Pair)) WHERE BuyerId={actorId} AND SellerId={candidate.SellerId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            actor = await Actor(actorId, ChatError.Forbidden, ct);
            if (actor.Role != "Buyer") throw Error(ChatError.Forbidden);
            var resolved = await Resolve(actorId, productId, orderItemId, ct);
            if (resolved.SellerId != candidate.SellerId) throw Error(ChatError.Conflict);
            if (!Active(await UserAsync(resolved.SellerId, ct), "Seller")) throw Error(ChatError.Conflict);
            if (c == null)
            {
                var now = DateTime.UtcNow;
                c = new() { BuyerId = actorId, SellerId = resolved.SellerId, CreatedAtUtc = now, UpdatedAtUtc = now,
                    ContextProductId = resolved.Context.ProductId, ContextOrderItemId = resolved.Context.OrderItemId,
                    ContextTitle = resolved.Context.Title, ContextUnitPrice = resolved.Context.UnitPrice };
                db.Add(c);
                await db.SaveChangesAsync(ct);
            }
            var result = new ChatOpenResult(await ConversationDto(c, actorId, ct), resolved.Context);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task<ChatInboxPage> InboxAsync(int actorId, ChatInboxCursor? cursor = null, int limit = ChatLimits.DefaultInboxSize, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var actor = await Actor(actorId, ChatError.Forbidden, ct);
        if (limit is < 1 or > ChatLimits.MaxInboxSize || cursor != null && (cursor.ConversationId <= 0 || cursor.UpdatedAtUtc.Offset != TimeSpan.Zero)) throw Error(ChatError.Invalid);
        var rows = db.Set<ChatConversation>().AsNoTracking().Where(x => actor.Role == "Buyer" ? x.BuyerId == actorId : x.SellerId == actorId);
        if (cursor != null)
        {
            var time = cursor.UpdatedAtUtc.UtcDateTime;
            rows = rows.Where(x => x.UpdatedAtUtc < time || x.UpdatedAtUtc == time && x.Id < cursor.ConversationId);
        }
        // Q-OPS-001: one statement for the page and its unread/peer/preview columns (was 3 queries per conversation, ~62 round trips,
        // whose tail passed 5 s on a CPU-saturated 2-vCPU CI runner during rolling updates). Same rules as ConversationDto above.
        var page = await rows.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id).Take(limit + 1).Select(c => new
        {
            Conversation = c,
            Unread = db.Set<Message>().LongCount(x => x.ConversationId == c.Id && x.SenderId != actorId &&
                x.Sequence > (actorId == c.BuyerId ? c.BuyerReadSequence : c.SellerReadSequence)),
            PeerName = db.Set<User>().Where(x => x.Id == (actorId == c.BuyerId ? c.SellerId : c.BuyerId)).Select(x => x.Username).FirstOrDefault(),
            Latest = db.Set<Message>().Where(x => x.ConversationId == c.Id && x.Sequence <= c.LastSequence).OrderByDescending(x => x.Sequence)
                .Select(x => new { Preview = x.Content!.Substring(0, x.Content.Length > 120 ? 120 : x.Content.Length), x.SentAtUtc }).FirstOrDefault()
        }).ToListAsync(ct);
        var hasMore = page.Count > limit;
        if (hasMore) page.RemoveAt(limit);
        var dtos = page.Select(x => ConversationDto(x.Conversation, actorId, x.Unread, x.PeerName, x.Latest?.Preview, x.Latest?.SentAtUtc)).ToList();
        var last = page.LastOrDefault()?.Conversation;
        await transaction.CommitAsync(ct);
        return new(dtos, hasMore && last != null ? new(Utc(last.UpdatedAtUtc), last.Id) : null);
    }

    public async Task<ChatHistoryPage> HistoryAsync(int actorId, int conversationId, ChatHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var c = await Owned(actorId, conversationId, false, ct);
        if (query.Limit is < 1 or > ChatLimits.MaxHistorySize || query.Before != null && query.After != null) throw Error(ChatError.Invalid);
        long before = 0, after = 0;
        if (query.Before != null && (!ChatCursor.TryParse(query.Before, out before) || before == 0) ||
            query.After != null && (!ChatCursor.TryParse(query.After, out after) || after > c.LastSequence)) throw Error(ChatError.Invalid);
        var rows = db.Set<Message>().AsNoTracking().Where(x => x.ConversationId == conversationId && x.Sequence <= c.LastSequence);
        if (query.Before != null) rows = rows.Where(x => x.Sequence < before);
        if (query.After != null) rows = rows.Where(x => x.Sequence > after);
        var page = await (query.After != null ? rows.OrderBy(x => x.Sequence) : rows.OrderByDescending(x => x.Sequence)).Take(query.Limit + 1).ToListAsync(ct);
        var hasMore = page.Count > query.Limit;
        if (hasMore) page.RemoveAt(query.Limit);
        if (query.After == null) page.Reverse();
        var messages = page.Select(MessageDto).ToArray();
        await transaction.CommitAsync(ct);
        if (query.After != null) logger.LogInformation(
            "Chat catch-up conversation={ConversationId} after={After} newest={Newest} watermark={HighWatermark} count={Count} more={HasMore} utc={Utc} requestTrace={RequestTraceId}",
            conversationId, query.After, messages.LastOrDefault()?.Sequence, ChatCursor.Format(c.LastSequence), messages.Length, hasMore,
            DateTimeOffset.UtcNow, Activity.Current?.TraceId.ToString());
        return new(messages, messages.FirstOrDefault()?.Sequence, messages.LastOrDefault()?.Sequence, hasMore, ChatCursor.Format(c.LastSequence));
    }

    private static string Normalize(string? text)
    {
        if (text == null) throw Error(ChatError.Invalid);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length is < 1 or > ChatLimits.MaxTextLength) throw Error(ChatError.Invalid);
        // Reject malformed UTF-16 instead of hashing distinct strings through replacement characters.
        for (var i = 0; i < normalized.Length; i++)
            if (char.IsSurrogate(normalized[i]) && (!char.IsHighSurrogate(normalized[i]) || ++i >= normalized.Length || !char.IsLowSurrogate(normalized[i]))) throw Error(ChatError.Invalid);
        return normalized;
    }
    public async Task<ChatSendReceipt> SendAsync(int actorId, int conversationId, ChatSendCommand command, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var c = await Owned(actorId, conversationId, true, ct);
            if (command.ClientMessageId == Guid.Empty || command.ProductId is <= 0 || command.OrderItemId is <= 0) throw Error(ChatError.Invalid);
            var text = Normalize(command.Content);
            var canonical = (command.ProductId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "\n" +
                (command.OrderItemId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "\n" + text;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            var existing = await db.Set<Message>().AsNoTracking().SingleOrDefaultAsync(x => x.ConversationId == conversationId && x.SenderId == actorId && x.ClientMessageId == command.ClientMessageId, ct);
            if (existing != null)
            {
                if (!CryptographicOperations.FixedTimeEquals(existing.PayloadHash!, hash)) throw Error(ChatError.Conflict);
                await transaction.CommitAsync(ct);
                Log(existing, true);
                return new(MessageDto(existing), true);
            }
            await RequirePeer(c, actorId, ct);
            ChatContext? context = null;
            if (command.ProductId != null || command.OrderItemId != null)
            {
                var resolved = await Resolve(c.BuyerId, command.ProductId, command.OrderItemId, ct, forSend: true);
                if (resolved.SellerId != c.SellerId) throw Error(ChatError.Conflict);
                context = resolved.Context;
            }
            if (c.LastSequence == long.MaxValue) throw Error(ChatError.Conflict);
            var now = DateTime.UtcNow;
            var message = new Message { SenderId = actorId, ReceiverId = actorId == c.BuyerId ? c.SellerId : c.BuyerId,
                ConversationId = c.Id, ConversationBuyerId = c.BuyerId, ConversationSellerId = c.SellerId,
                Sequence = checked(c.LastSequence + 1), ClientMessageId = command.ClientMessageId, Content = text, PayloadHash = hash,
                Timestamp = now, SentAtUtc = now, TraceId = (Activity.Current?.TraceId ?? ActivityTraceId.CreateRandom()).ToString(),
                ContextProductId = context?.ProductId, ContextOrderItemId = context?.OrderItemId, ContextTitle = context?.Title, ContextUnitPrice = context?.UnitPrice };
            c.LastSequence = message.Sequence.Value; c.UpdatedAtUtc = now;
            await db.Set<ChatConversation>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(x => x
                .SetProperty(row => row.LastSequence, c.LastSequence).SetProperty(row => row.UpdatedAtUtc, now), ct);
            db.Add(message);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            Log(message, false);
            return new(MessageDto(message), false);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        finally { db.ChangeTracker.Clear(); }
    }
    private void Log(Message message, bool replayed) => logger.LogInformation(
        "Chat receipt conversation={ConversationId} message={MessageId} sequence={Sequence} utc={SentAtUtc} trace={TraceId} replayed={Replayed} requestTrace={RequestTraceId}",
        message.ConversationId, message.Id, message.Sequence, message.SentAtUtc, message.TraceId, replayed, Activity.Current?.TraceId.ToString());

    public async Task<ChatReadReceipt> MarkReadAsync(int actorId, int conversationId, string throughSequence, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var c = await Owned(actorId, conversationId, true, ct);
            if (!ChatCursor.TryParse(throughSequence, out var through) || through > c.LastSequence) throw Error(ChatError.Invalid);
            var previous = actorId == c.BuyerId ? c.BuyerReadSequence : c.SellerReadSequence;
            if (actorId == c.BuyerId) c.BuyerReadSequence = Math.Max(c.BuyerReadSequence, through);
            else c.SellerReadSequence = Math.Max(c.SellerReadSequence, through);
            await db.Set<ChatConversation>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(x => x
                .SetProperty(row => row.BuyerReadSequence, c.BuyerReadSequence).SetProperty(row => row.SellerReadSequence, c.SellerReadSequence), ct);
            await transaction.CommitAsync(ct);
            return new(c.Id, actorId, ChatCursor.Format(actorId == c.BuyerId ? c.BuyerReadSequence : c.SellerReadSequence), through > previous);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        finally { db.ChangeTracker.Clear(); }
    }
}
