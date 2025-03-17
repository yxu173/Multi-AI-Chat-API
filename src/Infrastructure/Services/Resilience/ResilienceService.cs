using System.Net.Http;
using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Infrastructure.Services.Resilience
{
 
    public class ResilienceService : IResilienceService
    {
        private readonly ResilienceOptions _options;
        private readonly ILogger<ResilienceService> _logger;

        public ResilienceService(ResilienceOptions options, ILogger<ResilienceService> logger)
        {
            _options = options;
            _logger = logger;
        }

       
        public ResiliencePipeline<T> CreatePluginResiliencePipeline<T>() where T : class
        {
            var builder = new ResiliencePipelineBuilder<T>();
            
            // Add a retry strategy
            builder.AddRetry(new RetryStrategyOptions<T>
            {
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<TaskCanceledException>(),
                    
                MaxRetryAttempts = _options.RetryPolicy.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(_options.RetryPolicy.InitialDelayInSeconds),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retrying operation after failure. Attempt {Attempt} of {MaxAttempts}. Delay: {Delay}s",
                        args.AttemptNumber,
                        _options.RetryPolicy.MaxRetryAttempts,
                        args.RetryDelay.TotalSeconds);
                    
                    return ValueTask.CompletedTask;
                }
            });
            
            // Add a timeout strategy
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(_options.TimeoutPolicy.TimeoutInSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning(
                        "Operation timed out after {Timeout}s",
                        args.Timeout.TotalSeconds);
                    
                    return ValueTask.CompletedTask;
                }
            });
            
            return builder.Build();
        }
        
      
        public ResiliencePipeline<PluginResult> CreatePluginExecutionPipeline(Func<PluginResult, bool> isTransientError)
        {
            var builder = new ResiliencePipelineBuilder<PluginResult>();
            
            // Add a retry strategy with result-based handling
            builder.AddRetry(new RetryStrategyOptions<PluginResult>
            {
                ShouldHandle = new PredicateBuilder<PluginResult>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => !r.Success && isTransientError(r)),
                    
                MaxRetryAttempts = _options.RetryPolicy.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(_options.RetryPolicy.InitialDelayInSeconds),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retrying plugin execution after failure. Attempt {Attempt} of {MaxAttempts}. Delay: {Delay}s",
                        args.AttemptNumber,
                        _options.RetryPolicy.MaxRetryAttempts,
                        args.RetryDelay.TotalSeconds);
                    
                    return ValueTask.CompletedTask;
                }
            });
            
            // Add a timeout strategy
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(_options.TimeoutPolicy.TimeoutInSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning(
                        "Plugin execution timed out after {Timeout}s",
                        args.Timeout.TotalSeconds);
                    
                    return ValueTask.CompletedTask;
                }
            });
            
            return builder.Build();
        }
    }
}
