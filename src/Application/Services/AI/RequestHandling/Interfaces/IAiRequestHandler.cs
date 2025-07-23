namespace Application.Services.AI.RequestHandling.Interfaces;

public interface IAiRequestHandler
{
    Task<AiRequestPayload> PrepareRequestPayloadAsync(
        AiRequestContext context,
        CancellationToken cancellationToken = default);
}
