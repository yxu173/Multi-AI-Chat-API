using Serilog;
using Application;
using Infrastructure;
using Infrastructure.Database;
using Web.Api;
using Web.Api.Hubs;
using System.Runtime;
using FastEndpoints;
using FastEndpoints.Swagger;
using Application.Abstractions.PreProcessors;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using Web.Api.Extensions;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Console;
using Web.Api.Authorisation;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using Application.Services.Files.BackgroundProcessing;

GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

var builder = WebApplication.CreateBuilder(args);

var otelResourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: "Multi-AI-Chat-API", serviceVersion: "1.0.0");

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(otelResourceBuilder);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
  //  options.AddConsoleExporter();
});

builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("*", LogLevel.Warning);

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddPresentation(builder.Configuration)
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration);



builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
}).AddJsonProtocol();

builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        builder => builder
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddHttpClient("DefaultClient", client => { client.Timeout = TimeSpan.FromSeconds(30); })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

builder.Services.AddHttpClient();

builder.Services.AddOpenApi();

// // TODO: For richer health checks, install AspNetCore.HealthChecks.Npgsql and AspNetCore.HealthChecks.Redis NuGet packages
// builder.Services.AddHealthChecks()
//     .AddCheck("postgres", () => HealthCheckResult.Healthy("Postgres check placeholder"))
//     .AddCheck("redis", () => HealthCheckResult.Healthy("Redis check placeholder"));

var app = builder.Build();
app.MapPrometheusScrapingEndpoint().AllowAnonymous();

var uploadsPath = builder.Configuration["FilesStorage:BasePath"] ??
                  Path.Combine(Directory.GetCurrentDirectory(), "uploads");

if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
    Console.WriteLine($"Created file uploads directory at: {uploadsPath}");
}
else
{
    Console.WriteLine($"Using existing file uploads directory at: {uploadsPath}");
}

try
{
    var testFilePath = Path.Combine(uploadsPath, ".write_test");
    File.WriteAllText(testFilePath, "Write test");
    File.Delete(testFilePath);
    Console.WriteLine("File uploads directory has proper write permissions");
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: File uploads directory does not have proper write permissions: {ex.Message}");
}

//app.UseSecurityHeaders();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    //app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    var planRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionPlanRepository>();
    var freePlan = await planRepo.GetByNameAsync("Free Tier", default);
    if (freePlan == null)
    {
        var newFreePlan = SubscriptionPlan.CreateFreeTier();
        await planRepo.AddAsync(newFreePlan, default);
    }
}

app.UseSerilogRequestLogging();


app.UseStaticFiles();


app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.UseExceptionHandler();
app.UseCors("CorsPolicy");
//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");
app.UseFastEndpoints()
    .UseSwaggerGen();

// Add health check endpoint
// app.MapHealthChecks("/health");

app.Run();