using Application.Services.AI.Interfaces;

namespace Application.Services.AI.Builders;

public interface IPayloadBuilderFactory
{
    IAiRequestBuilder CreateOpenAiBuilder();
    IAiRequestBuilder CreateAnthropicBuilder();
    IAiRequestBuilder CreateGeminiBuilder();
    IAiRequestBuilder CreateDeepSeekBuilder();
    IAiRequestBuilder CreateAimlFluxBuilder();
    IAiRequestBuilder CreateImagenBuilder();
    IAiRequestBuilder CreateGrokBuilder();
    IAiRequestBuilder CreateQwenBuilder();
    IAiRequestBuilder CreateOpenAiDeepResearchBuilder();
}