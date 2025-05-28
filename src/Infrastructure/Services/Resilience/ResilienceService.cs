using System.Net.Http;
using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System;
using System.Threading.Tasks;

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
            
            builder.AddRetry(new RetryStrategyOptions<T>
            {
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<HttpRequestException>()
                    .Handle<System.TimeoutException>()
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
            
            builder.AddRetry(new RetryStrategyOptions<PluginResult>
            {
                ShouldHandle = new PredicateBuilder<PluginResult>()
                    .Handle<HttpRequestException>()
                    .Handle<System.TimeoutException>()
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

        public ResiliencePipeline<HttpResponseMessage> CreateAiServiceProviderPipeline(string providerName)
        {
            var pipelineBuilder = new ResiliencePipelineBuilder<HttpResponseMessage>();

            pipelineBuilder
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>()
                        .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                        .HandleResult(response => response != null && !response.IsSuccessStatusCode && (int)response.StatusCode >= 500),
                    MaxRetryAttempts = _options.RetryPolicy.MaxRetryAttempts,
                    Delay = TimeSpan.FromSeconds(_options.RetryPolicy.InitialDelayInSeconds),
                    BackoffType = DelayBackoffType.Exponential,
                    MaxDelay = TimeSpan.FromSeconds(_options.RetryPolicy.MaxDelayInSeconds),
                    OnRetry = args =>
                    {
                        _logger.LogWarning(
                            "Retrying AI Provider '{ProviderName}' call. Attempt {AttemptNumber}/{MaxRetryAttempts}. Delay: {RetryDelay:N1}s. Outcome: {Outcome}",
                            providerName, args.AttemptNumber, _options.RetryPolicy.MaxRetryAttempts, args.RetryDelay.TotalSeconds, args.Outcome.Exception?.GetType().Name ?? args.Outcome.Result?.StatusCode.ToString());
                        return ValueTask.CompletedTask;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>()
                        .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                        .HandleResult(response => response != null && !response.IsSuccessStatusCode && (int)response.StatusCode >= 500),
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(60),
                    OnOpened = args =>
                    {
                        _logger.LogWarning("Circuit breaker for AI Provider '{ProviderName}' opened for {BreakDuration:N1}s due to: {Outcome}",
                            providerName, args.BreakDuration.TotalSeconds, args.Outcome.Exception?.GetType().Name ?? args.Outcome.Result?.StatusCode.ToString());
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = args =>
                    {
                        _logger.LogInformation("Circuit breaker for AI Provider '{ProviderName}' closed. Outcome: {Outcome}",
                            providerName, args.Outcome.Exception?.GetType().Name ?? args.Outcome.Result?.StatusCode.ToString());
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = args =>
                    {
                        _logger.LogInformation("Circuit breaker for AI Provider '{ProviderName}' is now half-open.", providerName);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(_options.TimeoutPolicy.TimeoutInSeconds),
                    OnTimeout = args =>
                    {
                        _logger.LogWarning(
                            "AI Provider '{ProviderName}' call timed out after {Timeout:N1}s",
                            providerName, args.Timeout.TotalSeconds);
                        return ValueTask.CompletedTask;
                    }
                });

            return pipelineBuilder.Build();
        }
    }
}
