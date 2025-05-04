namespace Application.Services.AI.PayloadBuilders;

public interface IPayloadBuilderFactory
{
    IOpenAiPayloadBuilder CreateOpenAiBuilder();
    IAnthropicPayloadBuilder CreateAnthropicBuilder();
    IGeminiPayloadBuilder CreateGeminiBuilder();
    IDeepSeekPayloadBuilder CreateDeepSeekBuilder();
    IAimlFluxPayloadBuilder CreateAimlFluxBuilder();
    IPayloadBuilder CreateImagenBuilder();
    IGrokPayloadBuilder CreateGrokBuilder();
} 