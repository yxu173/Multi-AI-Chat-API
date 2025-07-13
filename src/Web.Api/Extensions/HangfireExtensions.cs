using Application.Services.Files.BackgroundProcessing;
using Hangfire;
using Hangfire.Console;
using Hangfire.PostgreSql;
using SixLabors.ImageSharp;

namespace Web.Api.Extensions;

public static class HangfireExtensions
{
    public static IServiceCollection AddHangfireExtensions(this IServiceCollection services, IConfiguration Configuration)
    {
        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(Configuration.GetConnectionString("Database"));
            })
            .UseConsole());

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = Environment.ProcessorCount * 2;
            options.Queues = new[] { "default", "critical", "low" };
            options.SchedulePollingInterval = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<BackgroundFileProcessor>();
        return services;
    }
}