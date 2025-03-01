using Application.Services;
using Domain.Aggregates.Chats;

namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history);
}