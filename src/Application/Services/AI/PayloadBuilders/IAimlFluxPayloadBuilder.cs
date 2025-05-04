namespace Application.Services.AI.PayloadBuilders;

public interface IAimlFluxPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context);
} 