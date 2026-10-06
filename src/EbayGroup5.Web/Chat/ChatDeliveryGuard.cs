using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Web.Chat;

// Recipient authorization at enqueue; never use a broad group containing stale principals.
public sealed class ChatDeliveryGuard(EbayDbContext db)
{
    private static bool Active(User? user, string role) => user?.Role == role && !string.IsNullOrEmpty(user.Password);
    private static string Group(ChatConversation c, User user)
    {
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            user.Id.ToString(CultureInfo.InvariantCulture) + "\n" + user.Role + "\n" + user.Password))).ToLowerInvariant();
        return $"chat:{c.Id}:member:{user.Id}:{stamp}";
    }
    private Task<ChatConversation?> Conversation(int id, CancellationToken ct) => db.Set<ChatConversation>()
        .FromSqlInterpolated($"SELECT * FROM dbo.ChatConversation WITH (HOLDLOCK) WHERE Id={id}").AsNoTracking().SingleOrDefaultAsync(ct);
    private Task<User?> User(int id, CancellationToken ct) => db.Set<User>()
        .FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={id}").AsNoTracking().SingleOrDefaultAsync(ct);

    public async Task<string> SubscribeAsync(int actorId, string principalRole, int conversationId, Func<string, Task> subscribe, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var c = await Conversation(conversationId, ct);
        var user = await User(actorId, ct);
        if (c == null || !Active(user, principalRole) || !(principalRole == "Buyer" && c.BuyerId == actorId || principalRole == "Seller" && c.SellerId == actorId))
            throw new ChatException(ChatError.NotFound);
        var group = Group(c, user!);
        await subscribe(group);
        await transaction.CommitAsync(ct);
        return group;
    }

    public async Task PublishAsync(int conversationId, Func<IReadOnlyList<string>, Task> publish, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var c = await Conversation(conversationId, ct);
        if (c == null) return;
        var buyer = await User(c.BuyerId, ct);
        var seller = await User(c.SellerId, ct);
        var groups = new List<string>(2);
        if (Active(buyer, "Buyer")) groups.Add(Group(c, buyer!));
        if (Active(seller, "Seller")) groups.Add(Group(c, seller!));
        if (groups.Count > 0) await publish(groups);
        await transaction.CommitAsync(ct);
    }
}
