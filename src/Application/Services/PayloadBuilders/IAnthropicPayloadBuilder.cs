using Application.Services;

namespace Application.Services.PayloadBuilders;

public interface IAnthropicPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions);
} 