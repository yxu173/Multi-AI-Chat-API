namespace Application.Services.AI.PayloadBuilders;

public interface IAnthropicPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions);
} 