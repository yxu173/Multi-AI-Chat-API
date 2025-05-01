namespace Application.Features.Chats.GetChatDetails;

public sealed record ChatDetailsResponse(
    DateTime CreatedAt,
    string ModelName,
    string CurrentContextLength,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal EstimatedCost);