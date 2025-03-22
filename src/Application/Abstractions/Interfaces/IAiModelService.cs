using Application.Services;

namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history,
        CancellationToken cancellationToken);
}

public record StreamResponse(string Content, int InputTokens, int OutputTokens);