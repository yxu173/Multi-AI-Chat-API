namespace Application.Services.AI.PayloadBuilders;

public interface IGrokPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context);
} 