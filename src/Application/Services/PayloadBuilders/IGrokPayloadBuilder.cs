namespace Application.Services.PayloadBuilders;

public interface IGrokPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context);
} 