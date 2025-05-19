using FastEndpoints;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Application.Abstractions.PreProcessors;

public class RequestLoggingPreProcessor<TRequest> : IPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly ILogger<RequestLoggingPreProcessor<TRequest>> _logger;

    public RequestLoggingPreProcessor(ILogger<RequestLoggingPreProcessor<TRequest>> logger)
    {
        _logger = logger;
    }

    public async Task PreProcessAsync(IPreProcessorContext<TRequest> ctx, CancellationToken ct)
    {
        string requestName = typeof(TRequest).Name;
        _logger.LogInformation("Handling {RequestName}", requestName);

        // Store the request name in HttpContext items to be used in post-processing
        ctx.HttpContext.Items["RequestName"] = requestName;

        // Execute the handler and check result later in a post-processor
        await Task.CompletedTask;
    }
}
