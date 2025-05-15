using Application.Services.Messaging;

namespace Application.Services.AI.RequestHandling.Interfaces;

public interface IHistoryProcessor
{
    Task<List<MessageDto>> ProcessAsync(
        IEnumerable<MessageDto> history,
        CancellationToken cancellationToken = default);
}
