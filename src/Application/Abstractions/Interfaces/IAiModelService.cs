using Application.Services;

namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history,
        Action<int, int>? tokenCallback = null);
}