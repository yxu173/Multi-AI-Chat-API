 using FastEndpoints;

namespace Application.Notifications;

public class TokenUsageUpdatedNotification : IEvent
{
    public Guid ChatSessionId { get; }
    public int InputTokens { get; }
    public int OutputTokens { get; }
    public decimal TotalCost { get; }

    public TokenUsageUpdatedNotification(
        Guid chatSessionId,
        int inputTokens, 
        int outputTokens,
        decimal totalCost)
    {
        ChatSessionId = chatSessionId;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalCost = totalCost;
    }
}