using Domain.Enums;

namespace Application.Services.PayloadBuilders;

public interface IPayloadBuilderFactory
{
    IOpenAiPayloadBuilder CreateOpenAiBuilder();
    IAnthropicPayloadBuilder CreateAnthropicBuilder();
    IGeminiPayloadBuilder CreateGeminiBuilder();
    IDeepSeekPayloadBuilder CreateDeepSeekBuilder();
    IAimlFluxPayloadBuilder CreateAimlFluxBuilder();
    IPayloadBuilder CreateImagenBuilder();
} 