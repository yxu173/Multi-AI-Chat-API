using Application.Services;

namespace Application.Services.PayloadBuilders;

public interface IAimlFluxPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context);
} 