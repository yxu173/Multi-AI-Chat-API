using Application.Services;

namespace Application.Services.PayloadBuilders;

public interface IGrokPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions);
} 