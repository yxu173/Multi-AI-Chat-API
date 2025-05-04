namespace Application.Services.AI.PayloadBuilders;

public interface IQwenPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context);
} 