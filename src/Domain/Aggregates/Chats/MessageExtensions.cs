using Domain.Enums;

namespace Domain.Aggregates.Chats;

public static class MessageExtensions
{
    /// <summary>
    /// Returns true if the message is in a terminal state (Completed, Interrupted or Failed).
    /// </summary>
    public static bool IsTerminal(this Message message)
    {
        return message.Status == MessageStatus.Completed ||
               message.Status == MessageStatus.Interrupted ||
               message.Status == MessageStatus.Failed;
    }
}
