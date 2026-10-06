using System.Security.Claims;
using EbayGroup5.Application.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EbayGroup5.Web.Chat;

public interface IChatClient
{
    Task MessageCommitted(ChatMessageDto message);
    Task ReadAdvanced(ChatReadReceipt receipt);
}

[Authorize(Roles = "Buyer,Seller")]
public sealed class ChatHub(IChatService chat, ChatRealtime realtime, ChatDeliveryGuard guard) : Hub<IChatClient>
{
    private int ActorId => int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Dictionary<int, string> Subscriptions
    {
        get
        {
            if (!Context.Items.TryGetValue(nameof(Subscriptions), out var subscriptions))
                Context.Items[nameof(Subscriptions)] = subscriptions = new Dictionary<int, string>();
            return (Dictionary<int, string>)subscriptions!;
        }
    }
    public async Task<ChatConversationDto> JoinConversation(int conversationId)
    {
        if (Subscriptions.Count >= 50 && !Subscriptions.ContainsKey(conversationId)) throw new ChatException(ChatError.Invalid);
        var conversation = await chat.ConversationAsync(ActorId, conversationId, Context.ConnectionAborted);
        var group = await guard.SubscribeAsync(ActorId, Context.User!.FindFirstValue(ClaimTypes.Role)!, conversationId,
            name => Groups.AddToGroupAsync(Context.ConnectionId, name, Context.ConnectionAborted), Context.ConnectionAborted);
        if (Subscriptions.TryGetValue(conversationId, out var old) && old != group)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, old, Context.ConnectionAborted);
        Subscriptions[conversationId] = group;
        return conversation;
    }
    public async Task LeaveConversation(int conversationId)
    {
        if (Subscriptions.Remove(conversationId, out var group)) await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }
    public Task<ChatSendReceipt> SendMessage(int conversationId, ChatSendCommand command) =>
        command == null ? throw new ChatException(ChatError.Invalid) : realtime.SendAsync(ActorId, conversationId, command, Context.ConnectionAborted);
    public Task<ChatReadReceipt> MarkRead(int conversationId, string throughSequence) =>
        realtime.ReadAsync(ActorId, conversationId, throughSequence, Context.ConnectionAborted);
}
