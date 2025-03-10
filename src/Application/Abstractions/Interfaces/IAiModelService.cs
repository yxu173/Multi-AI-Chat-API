using Application.Services;

namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        Action<int, int>? tokenCallback = null);
    // Task<(int InputTokens, int OutputTokens)> CountTokensAsync(IEnumerable<MessageDto> messages);
}

public record StreamResponse(string Content, int InputTokens, int OutputTokens);