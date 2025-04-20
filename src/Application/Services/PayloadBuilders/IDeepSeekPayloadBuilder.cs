using Application.Services;

namespace Application.Services.PayloadBuilders;

public interface IDeepSeekPayloadBuilder
{
     Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<object>? toolDefinitions,
        CancellationToken cancellationToken);
} 