using Application.Services;

namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history);
    Task<TokenUsage> CountTokensAsync(IEnumerable<MessageDto> messages);
}

public record TokenUsage(int InputTokens, int OutputTokens, decimal TotalCost);