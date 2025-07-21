using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Configuration;

namespace Web.Api.Extensions;

public static class LoggingExtensions
{
    public static IServiceCollection AddOpenTelemetryLogger(this IServiceCollection services, IConfiguration configuration)
    {
        var otelResourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: "Multi-AI-Chat-API", serviceVersion: "1.0.0");

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        var otlpApiKey = configuration["OpenTelemetry:ApiKey"];

        OpenTelemetryServicesExtensions.AddOpenTelemetry(services)
            .WithLogging(loggerProviderBuilder =>
                loggerProviderBuilder
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri($"{otlpEndpoint}logs");
                        options.Headers = $"Authorization=Bearer {otlpApiKey}";
                    })
            )
            .WithTracing(tracerProviderBuilder =>
                tracerProviderBuilder
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddSource("Multi-AI-Chat-API.Web.Api")
                    .AddAspNetCoreInstrumentation(options => { options.RecordException = true; })
                    .AddHttpClientInstrumentation(options => { options.RecordException = true; })
                    .AddEntityFrameworkCoreInstrumentation(options => { options.SetDbStatementForText = true; })
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri($"{otlpEndpoint}traces");
                        options.Headers = $"Authorization=Bearer {otlpApiKey}";
                    })
            )
            .WithMetrics(meterProviderBuilder =>
                meterProviderBuilder
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri($"{otlpEndpoint}metrics");
                        options.Headers = $"Authorization=Bearer {otlpApiKey}";
                    })
            );
        return services;
    }
}