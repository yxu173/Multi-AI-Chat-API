namespace Application.Services.AI.PayloadBuilders;

public interface IOpenAiPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions);
} 