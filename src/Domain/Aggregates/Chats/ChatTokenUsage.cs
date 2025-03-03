using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class ChatTokenUsage : BaseEntity
{
    public Guid MessageId { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal TotalCost { get; private set; }
    public DateTime CreatedAt { get; private set; }
    
   
    public Message Message { get; private set; }
    
    private ChatTokenUsage() { } 
    
    public static ChatTokenUsage Create(Guid messageId, int inputTokens, int outputTokens, decimal totalCost)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId cannot be empty.", nameof(messageId));
        
        return new ChatTokenUsage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalCost = totalCost,
            CreatedAt = DateTime.UtcNow
        };
    }
}