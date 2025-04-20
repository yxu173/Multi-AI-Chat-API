using Polly;

namespace Application.Abstractions.Interfaces
{
    public interface IResilienceService
    {
        ResiliencePipeline<T> CreatePluginResiliencePipeline<T>() where T : class;


        ResiliencePipeline<PluginResult> CreatePluginExecutionPipeline(Func<PluginResult, bool> isTransientError);
    }
}