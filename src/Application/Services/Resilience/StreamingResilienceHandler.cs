using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Application.Services.AI;
using Application.Services.Streaming;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Application.Services.Resilience;

public class StreamingResilienceHandler : IStreamingResilienceHandler
{
    private readonly IProviderKeyManagementService _providerKeyManagementService;
    private readonly ILogger<StreamingResilienceHandler> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly StreamingOptions _options;

    public StreamingResilienceHandler(
        IProviderKeyManagementService providerKeyManagementService,
        ILogger<StreamingResilienceHandler> logger,
        IOptions<StreamingOptions> options)
    {
        _providerKeyManagementService = providerKeyManagementService;
        _logger = logger;
        _options = options.Value;
        _retryPolicy = CreateRetryPolicy();
    }

    public Task<TResult> ExecuteWithRetriesAsync<TResult>(
        Func<Task<TResult>> action,
        AiRequestContext requestContext,
        Message aiMessage,
        CancellationToken cancellationToken)
    {
        return _retryPolicy.ExecuteAsync(async (context, ct) =>
        {
            int attempt = (int)context["Attempt"];
            _logger.LogInformation(
                "Executing stream action for chat {ChatSessionId}, attempt {Attempt}",
                requestContext.ChatSession.Id, attempt);
            return await action();
        }, new Context($"Streaming-{requestContext.ChatSession.Id}") { ["Attempt"] = 1 }, cancellationToken);
    }
    
    private AsyncRetryPolicy CreateRetryPolicy()
    {
        return Policy
            .Handle<ProviderRateLimitException>()
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: _options.MaxRetries,
                sleepDurationProvider: (attempt, exception, context) =>
                {
                    TimeSpan delay;
                    if (exception is ProviderRateLimitException rateLimitEx && rateLimitEx.RetryAfter.HasValue)
                    {
                        delay = rateLimitEx.RetryAfter.Value;
                    }
                    else
                    {
                        delay = TimeSpan.FromSeconds(Math.Pow(_options.RetryBackoffFactor, attempt - 1) * _options.InitialRetryDelaySeconds);
                    }
                    _logger.LogInformation("Retrying stream action for chat {ChatSessionId}. Delaying for {Delay}ms.", context.CorrelationId, delay.TotalMilliseconds);
                    return delay;
                },
                onRetryAsync: async (exception, timespan, attempt, context) =>
                {
                    context["Attempt"] = attempt + 1;
                    _logger.LogWarning(exception,
                        "Stream action failed for chat {ChatSessionId} on attempt {Attempt}. Retrying in {Timespan}s.",
                        context.CorrelationId, attempt, timespan.TotalSeconds);

                    if (exception is ProviderRateLimitException rateLimitEx && rateLimitEx.ApiKeyIdUsed.HasValue)
                    {
                        await _providerKeyManagementService.ReportKeyRateLimitedAsync(
                            rateLimitEx.ApiKeyIdUsed.Value, 
                            rateLimitEx.RetryAfter ?? timespan, 
                            CancellationToken.None);
                    }
                }
            );
    }
} 