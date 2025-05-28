using Serilog;
using Application;
using Infrastructure;
using Infrastructure.Database;
using Web.Api;
using Web.Api.Hubs;
using Web.Api.Middleware;
using System.Runtime;
using System.Threading.RateLimiting;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.RateLimiting;
using Application.Abstractions.PreProcessors;
using Web.Api.Extensions;
using Polly;
using Polly.Extensions.Http;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Console;
using Web.Api.Authorisation;

GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Database"));
    })
    .UseConsole());

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
    options.Queues = new[] { "default", "critical", "low" };
});

builder.Services
    .AddPresentation()
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration);

builder.Services.AddFastEndpoints()
    .SwaggerDocument();

// Register global pre-processors and post-processors for FastEndpoints
builder.Services.AddSingleton(typeof(IPreProcessor<>), typeof(RequestLoggingPreProcessor<>));
builder.Services.AddSingleton(typeof(IPreProcessor<>), typeof(ValidationPreProcessor<>));
builder.Services.AddSingleton(typeof(IPostProcessor<,>), typeof(RequestLoggingPostProcessor<,>));

// Add security features: Rate limiting
builder.Services.AddApplicationRateLimiting();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        builder => builder
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddHttpClient("DefaultClient", client => {
    client.Timeout = TimeSpan.FromSeconds(30);
})
.SetHandlerLifetime(TimeSpan.FromMinutes(5)); 

builder.Services.AddHttpClient();

builder.Services.AddOpenApi();

var app = builder.Build();
await app.UseAdminManagedApiKeysAsync();


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

// Apply security headers for all responses
//app.UseSecurityHeaders();

// Enable rate limiting middleware
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
}

app.UseSerilogRequestLogging();

app.UseCors("CorsPolicy");

app.UseStaticFiles();

// Add Hangfire Dashboard middleware.
// Ensure it's placed after authentication/authorization if you want to secure it.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");
app.UseFastEndpoints()
    .UseSwaggerGen();
app.Run();