namespace Application.Services.AI.PayloadBuilders;

public interface IGeminiPayloadBuilder
{
    Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<object>? toolDefinitions,
        CancellationToken cancellationToken);
} 