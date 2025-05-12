using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices;
using Infrastructure.Services.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Application.Services;

public class AiModelServiceFactory : IAiModelServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IProviderKeyManagementService _keyManagementService;
    private readonly ILogger<AiModelServiceFactory> _logger;

    public AiModelServiceFactory(
        IServiceProvider serviceProvider, 
        IApplicationDbContext dbContext, 
        IConfiguration configuration,
        ISubscriptionService subscriptionService,
        IProviderKeyManagementService keyManagementService,
        ILogger<AiModelServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _dbContext = dbContext;
        _configuration = configuration;
        _subscriptionService = subscriptionService;
        _keyManagementService = keyManagementService;
        _logger = logger;
    }

    public IAiModelService GetService(Guid userId, Guid modelId, string? customApiKey = null, Guid? aiAgentId = null)
    {
        return GetServiceAsync(userId, modelId, customApiKey, aiAgentId, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task<IAiModelService> GetServiceAsync(Guid userId, Guid modelId, string? customApiKey, Guid? aiAgentId, CancellationToken cancellationToken = default)
    {
        var aiModel = await _dbContext.AiModels
                          .Include(m => m.AiProvider)
                          .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken) 
                      ?? throw new NotSupportedException($"No AI Model or Provider configured with ID: {modelId}");
        
        var apiKey = await GetApiKeyAsync(userId, aiModel.AiProviderId, customApiKey, cancellationToken);
        
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        var openAiLogger = _serviceProvider.GetService<ILogger<OpenAiService>>();
        var anthropicLogger = _serviceProvider.GetService<ILogger<AnthropicService>>();
        var deepSeekLogger = _serviceProvider.GetService<ILogger<DeepSeekService>>();
        var geminiLogger = _serviceProvider.GetService<ILogger<GeminiService>>();
        var aimlLogger = _serviceProvider.GetService<ILogger<AimlApiService>>();
        var imagenLogger = _serviceProvider.GetService<ILogger<ImagenService>>();
        var grokLogger = _serviceProvider.GetService<ILogger<GrokService>>();
        var qwenLogger = _serviceProvider.GetService<ILogger<QwenService>>();
        
        return aiModel.ModelType switch
        {
            ModelType.OpenAi => new OpenAiService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.Anthropic => new AnthropicService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.DeepSeek => new DeepSeekService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.Gemini => new GeminiService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.AimlFlux => new AimlApiService(httpClientFactory, apiKey, aiModel.ModelCode, aimlLogger),
            ModelType.Imagen => CreateImagenService(httpClientFactory, apiKey, aiModel.ModelCode, imagenLogger),
            ModelType.Grok => new GrokService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.Qwen => new QwenService(httpClientFactory, apiKey, aiModel.ModelCode),
            _ => throw new NotSupportedException($"Model type {aiModel.ModelType} not supported.")
        };
    }

    private ImagenService CreateImagenService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode, ILogger<ImagenService>? logger)
    {
        var projectId = _configuration["AI:Imagen:ProjectId"] ?? throw new InvalidOperationException("Imagen ProjectId not configured.");
        var region = _configuration["AI:Imagen:Region"] ?? throw new InvalidOperationException("Imagen Region not configured.");
        
        return new ImagenService(httpClientFactory, projectId, region, modelCode, logger);
    }

    private async Task<string> GetApiKeyAsync(Guid userId, Guid providerId, string? customApiKey, CancellationToken cancellationToken = default)
    {
        // If a custom API key is provided for this request, use it
        if (!string.IsNullOrEmpty(customApiKey)) 
        {
            return customApiKey;
        }
        
        // Check user quota before proceeding
        var (hasQuota, errorMessage) = await _subscriptionService.CheckUserQuotaAsync(userId, cancellationToken: cancellationToken);
        if (!hasQuota)
        {
            throw new QuotaExceededException(errorMessage ?? "Quota exceeded for user subscription.");
        }

        // Get an API key from the admin-managed key pool
        var apiKey = await _keyManagementService.GetAvailableApiKeyAsync(providerId, cancellationToken);
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        // If all else fails, check if there's a default key configured for the provider
        var provider = await _dbContext.AiProviders.FindAsync(new object[] { providerId }, cancellationToken) 
            ?? throw new Exception($"AI Provider with ID {providerId} not found.");
            
        return provider.DefaultApiKey 
            ?? throw new Exception($"No API keys available for provider {provider.Name}. Please try again later or contact support.");
    }
}