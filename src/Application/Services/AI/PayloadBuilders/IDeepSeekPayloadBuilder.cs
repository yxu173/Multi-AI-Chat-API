namespace Application.Services.AI.PayloadBuilders;

public interface IDeepSeekPayloadBuilder
{
     Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<object>? toolDefinitions,
        CancellationToken cancellationToken);
} 