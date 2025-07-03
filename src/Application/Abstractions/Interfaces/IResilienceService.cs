using Polly;

namespace Application.Abstractions.Interfaces
{
    public interface IResilienceService
    {
        ResiliencePipeline<T> CreatePluginResiliencePipeline<T>() where T : class;
        
        ResiliencePipeline<HttpResponseMessage> CreateAiServiceProviderPipeline(string providerName, TimeSpan? timeout = null);
    }
}