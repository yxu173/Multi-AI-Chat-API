using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Web.Api.Extensions;

public static class LoggingExtensions
{
    public static IServiceCollection AddOpenTelemetryLogger(this IServiceCollection services)
    {
        var otelResourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: "Multi-AI-Chat-API", serviceVersion: "1.0.0");

        OpenTelemetryServicesExtensions.AddOpenTelemetry(services)
            .WithLogging(loggerProviderBuilder =>
                loggerProviderBuilder
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddConsoleExporter()
            )
            .WithTracing(tracerProviderBuilder =>
                    tracerProviderBuilder
                        .SetResourceBuilder(otelResourceBuilder)
                        .AddSource("Multi-AI-Chat-API.Web.Api")
                        .AddAspNetCoreInstrumentation(options => { options.RecordException = true; })
                        .AddHttpClientInstrumentation(options => { options.RecordException = true; })
                        .AddEntityFrameworkCoreInstrumentation(options => { options.SetDbStatementForText = true; })
                //    .AddConsoleExporter()
            )
            .WithMetrics(meterProviderBuilder =>
                meterProviderBuilder
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddPrometheusExporter()
            );
        return services;
    }
}