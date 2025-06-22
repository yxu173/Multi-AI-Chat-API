using Application.Services.AI;
namespace Application.Services.Streaming;

public interface IStreamingContextService
{
    Task<AiRequestContext> BuildContextAsync(StreamingRequest request, CancellationToken cancellationToken);
} 