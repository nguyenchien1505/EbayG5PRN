namespace EbayGroup5.Application.Chat;

public enum ChatError { Invalid, Forbidden, NotFound, Conflict }

// Stable generic errors; never carry message content, SQL details or credentials.
public sealed class ChatException(ChatError error) : Exception(error switch
{
    ChatError.Invalid => "Invalid chat request.",
    ChatError.Forbidden => "This chat operation is not allowed.",
    ChatError.NotFound => "Conversation or context not found.",
    _ => "This chat operation conflicts with the current state."
})
{
    public ChatError Error { get; } = error;
}
