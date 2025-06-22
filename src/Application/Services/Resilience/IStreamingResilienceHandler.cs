using Application.Services.AI;
using Domain.Aggregates.Chats;

namespace Application.Services.Resilience;

public interface IStreamingResilienceHandler
{
    Task<TResult> ExecuteWithRetriesAsync<TResult>(
        Func<Task<TResult>> action,
        AiRequestContext requestContext,
        Message aiMessage,
        CancellationToken cancellationToken);
} 