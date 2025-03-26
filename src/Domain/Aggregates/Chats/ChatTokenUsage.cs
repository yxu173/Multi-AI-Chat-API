using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class ChatTokenUsage : BaseEntity
{
    public Guid ChatId { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal TotalCost { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastUpdatedAt { get; private set; }


    public ChatSession ChatSession { get; private set; }

    private ChatTokenUsage()
    {
    }

    public static ChatTokenUsage Create(Guid chatId, int inputTokens, int outputTokens, decimal totalCost)
    {
        if (chatId == Guid.Empty) throw new ArgumentException("chatId cannot be empty.", nameof(chatId));

        return new ChatTokenUsage
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalCost = totalCost,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateTokenCounts(int inputTokens, int outputTokens)
    {
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        LastUpdatedAt = DateTime.UtcNow;
    }

    public void UpdateTokenCountsAndCost(int inputTokens, int outputTokens, decimal totalCost)
    {
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        TotalCost += totalCost;
        LastUpdatedAt = DateTime.UtcNow;
    }
    
    public void SetTokenCountsAndCost(int inputTokens, int outputTokens, decimal totalCost)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalCost = totalCost;
        LastUpdatedAt = DateTime.UtcNow;
    }
}