using System.Globalization;
using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Authentication;
using Infrastructure.Authentication.Options;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.Services.Caching;
using Infrastructure.Services.Plugins;
using Infrastructure.Services.Resilience;
using Infrastructure.Services.Subscription;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Database")));

        services.AddHttpContextAccessor();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IFileAttachmentRepository, FileAttachmentRepository>();
        services.AddScoped<IPromptRepository, PromptRepository>();
        services.AddScoped<IChatTokenUsageRepository, ChatTokenUsageRepository>();
        services.AddScoped<IUserApiKeyRepository, UserApiKeyRepository>();
        services.AddScoped<IAiModelRepository, AiModelRepository>();
        services.AddScoped<IAiProviderRepository, AiProviderRepository>();
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<IUserPluginRepository, UserPluginRepository>();
        services.AddScoped<IChatSessionPluginRepository, ChatSessionPluginRepository>();
        services.AddScoped<IChatFolderRepository, ChatFolderRepository>();
        services.AddScoped<IUserAiModelSettingsRepository, UserAiModelSettingsRepository>();
        services.AddScoped<IAiAgentRepository, AiAgentRepository>();
        
        services.AddScoped<IProviderApiKeyRepository, ProviderApiKeyRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<IUserSubscriptionRepository, UserSubscriptionRepository>();

        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IProviderKeyManagementService, ProviderKeyManagementService>();
        services.AddScoped<IAiModelServiceFactory, AiModelServiceFactory>();
        services.AddScoped<IPluginExecutorFactory, PluginExecutorFactory>();
        
        services.AddHostedService<DailyQuotaResetService>();

        services.AddHttpClient();

        var webSearchConfig = configuration.GetSection("PluginSettings:WebSearch");
        services.AddScoped<WebSearchPlugin>(sp =>
            new WebSearchPlugin(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
                webSearchConfig["ApiKey"] ?? throw new InvalidOperationException("Missing WebSearch API key"),
                webSearchConfig["SearchEngine"] ?? throw new InvalidOperationException("Missing Search Engine ID")
            )
        );
        services.AddScoped<IChatPlugin, WebSearchPlugin>(sp =>
            sp.GetRequiredService<WebSearchPlugin>()
        );

        services.AddScoped<IChatPlugin, PerplexityPlugin>(sp =>
            new PerplexityPlugin(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
                configuration["Plugins:Perplexity:ApiKey"]
            )
        );

        services.AddScoped<JinaWebPlugin>(sp =>
            new JinaWebPlugin(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
                configuration["PluginSettings:Jina:ApiKey"] ??
                throw new InvalidOperationException("Missing Jina API key"),
                maxRetries: configuration.GetValue<int>("Plugins:Jina:MaxRetries", 3)
            )
        );
        services.AddScoped<IChatPlugin, JinaWebPlugin>(sp => 
            sp.GetRequiredService<JinaWebPlugin>()
        );

        // Register Hacker News Search Plugin
        services.AddScoped<HackerNewsSearchPlugin>(sp =>
            new HackerNewsSearchPlugin(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient()
            )
        );
        services.AddScoped<IChatPlugin, HackerNewsSearchPlugin>(sp =>
            sp.GetRequiredService<HackerNewsSearchPlugin>()
        );

        services.AddDistributedMemoryCache();
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConfig =
                ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
            redisConfig.AbortOnConnectFail = false;
            redisConfig.ConnectTimeout = 5000;
            redisConfig.SyncTimeout = 5000;
            redisConfig.ConnectRetry = 3;
            // Helps minimize memory fragmentation
            redisConfig.PreserveAsyncOrder = false;
            return ConnectionMultiplexer.Connect(redisConfig);
        });


        services.AddScoped<ICacheService, RedisCacheService>();

        var resilienceOptions = ResilienceOptions.FromConfiguration(configuration);
        services.AddSingleton(resilienceOptions);
        services.AddSingleton<IResilienceService, ResilienceService>();


        services.AddScoped<IUserContext, UserContext>();
        services.AddSingleton<ITokenProvider, TokenProvider>();
        services.AddScoped<IApplicationDbContext, ApplicationDbContext>();
        services.AddTransient<IEmailSender, EmailSender>(provider =>
        {
            IConfiguration configuration = provider.GetRequiredService<IConfiguration>();
            IConfigurationSection smtpSettings = configuration.GetSection("SmtpSettings");
            return new EmailSender(
                smtpSettings["Host"]!,
                int.Parse(smtpSettings["Port"]!, CultureInfo.InvariantCulture),
                smtpSettings["Username"]!,
                smtpSettings["Password"]!
            );
        });

        services.AddIdentity<User, Domain.Aggregates.Users.Role>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var jwtSettings = new JwtSettings();
        configuration.Bind(nameof(JwtSettings), jwtSettings);
        var jwtSection = configuration.GetSection(nameof(JwtSettings));
        services.Configure<JwtSettings>(jwtSection);

        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
        {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase = true;
            options.Password.RequiredLength = 6;
            options.Password.RequiredUniqueChars = 1;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        });


        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddJwtBearer(jwt =>
            {
                jwt.SaveToken = true;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey))
                };

                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            })
            .AddGoogle(googleOptions =>
            {
                googleOptions.ClientId = configuration["Authentication:Google:ClientId"]!;
                googleOptions.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;
                googleOptions.CallbackPath = "/signin-google";
                googleOptions.CorrelationCookie.SameSite = SameSiteMode.None;
                googleOptions.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                googleOptions.SaveTokens = true;
            });
        services.AddAuthorization();

        services.AddHttpClient("WikipediaApiClient", client =>
        {
            client.DefaultRequestHeaders.Add("Api-User-Agent", "MultiAiChatApi/1.0 (https://github.com/yourrepo/Multi-AI-Chat-API; your-email@example.com)");
        }).AddPolicyHandler(GetRetryPolicy());

        // Register Wikipedia Plugin
        services.AddScoped<WikipediaPlugin>(sp =>
            new WikipediaPlugin(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("WikipediaApiClient")
            )
        );
        services.AddScoped<IChatPlugin, WikipediaPlugin>(sp =>
            sp.GetRequiredService<WikipediaPlugin>()
        );

        // Register CSV Reader Plugin
        services.AddScoped<CsvReaderPlugin>(sp =>
            new CsvReaderPlugin(
                sp.GetRequiredService<IFileAttachmentRepository>(),
                sp.GetRequiredService<ILogger<CsvReaderPlugin>>()
            )
        );
        services.AddScoped<IChatPlugin, CsvReaderPlugin>(sp =>
            sp.GetRequiredService<CsvReaderPlugin>()
        );

        return services;
    }

    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // Handles HttpRequestException, 5XX and 408
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}