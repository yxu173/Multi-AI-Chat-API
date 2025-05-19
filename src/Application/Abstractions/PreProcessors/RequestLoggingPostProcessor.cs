using FastEndpoints;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SharedKernal;

namespace Application.Abstractions.PreProcessors;

public class RequestLoggingPostProcessor<TRequest, TResponse> : IPostProcessor<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly ILogger<RequestLoggingPostProcessor<TRequest, TResponse>> _logger;

    public RequestLoggingPostProcessor(ILogger<RequestLoggingPostProcessor<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public Task PostProcessAsync(IPostProcessorContext<TRequest, TResponse> ctx, CancellationToken ct)
    {
        // Retrieve the request name from HttpContext items that was set in the pre-processor
        var requestName = ctx.HttpContext.Items["RequestName"] as string ?? typeof(TRequest).Name;
        
        if (ctx.Response.IsSuccess)
        {
            _logger.LogInformation("{RequestName} handled successfully", requestName);
        }
        else
        {
            using (LogContext.PushProperty("Error", ctx.Response.Error, true))
            {
                _logger.LogError("Completed request {RequestName} with error", requestName);
            }
        }

        return Task.CompletedTask;
    }
}
