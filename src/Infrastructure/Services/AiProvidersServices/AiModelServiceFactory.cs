using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Application.Services.AI.Streaming;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Infrastructure.Services.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Services.AI.Interfaces;
using Domain.Aggregates.Llms;

namespace Infrastructure.Services.AiProvidersServices;

public class AiModelServiceFactory : IAiModelServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IProviderKeyManagementService _keyManagementService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiModelServiceFactory> _logger;
    private readonly ICacheService _cacheService;

    public AiModelServiceFactory(
        IServiceProvider serviceProvider,
        IApplicationDbContext dbContext,
        IConfiguration configuration,
        ISubscriptionService subscriptionService,
        IProviderKeyManagementService keyManagementService,
        IHttpClientFactory httpClientFactory,
        ILogger<AiModelServiceFactory> logger,
        ICacheService cacheService)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        _keyManagementService = keyManagementService ?? throw new ArgumentNullException(nameof(keyManagementService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    [Obsolete("Use GetServiceContextAsync instead for better error handling and API key management.")]
    public IAiModelService GetService(Guid userId, Guid modelId, Guid? aiAgentId = null)
    {
        var contextResult = GetServiceContextAsync(userId, modelId, aiAgentId, CancellationToken.None)
            .GetAwaiter().GetResult();
        return contextResult?.Service ??
               throw new Exception("Failed to get AI service from obsolete GetService method.");
    }

    public async Task<AiServiceContext?> GetServiceContextAsync(
        Guid userId,
        Guid modelId,
        Guid? aiAgentId,
        CancellationToken cancellationToken = default)
    {
        // string cacheKey = "aimodel:" + modelId;
        // var aiModel = await _cacheService.GetAsync<AiModel>(cacheKey, cancellationToken);
        
        _logger.LogDebug("Fetching AiModel {ModelId} from database.", modelId);
        var aiModel = await _dbContext.AiModels
                          .Include(m => m.AiProvider)
                          .AsNoTracking()
                          .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);

        if (aiModel == null)
        {
            throw new NotSupportedException($"No AI Model or Provider configured with ID: {modelId}");
        }

        _logger.LogDebug("AiModel {ModelId} found in database. Provider: {ProviderId}, ProviderName: {ProviderName}", 
            modelId, aiModel.AiProviderId, aiModel.AiProvider?.Name ?? "NULL");

        var (hasQuota, quotaErrorMessage) = await _subscriptionService.CheckUserQuotaAsync(userId, aiModel.RequestCost, cancellationToken: cancellationToken);
        if (!hasQuota)
        {
            throw new QuotaExceededException(quotaErrorMessage ?? "Quota exceeded for user subscription.");
        }

        string? apiKeySecretToUse = null;

        var managedApiKey = await _keyManagementService.GetProviderApiKeyObjectAsync(aiModel.AiProviderId, cancellationToken);
        if (managedApiKey != null)
        {
            apiKeySecretToUse = managedApiKey.Secret;
            _logger.LogInformation("Using managed API key {ApiKeyId} for provider {ProviderId}", managedApiKey.Id,
                aiModel.AiProviderId);
        }
        else
        {
            _logger.LogWarning(
                "No managed API key available from pool for provider {ProviderId}. Falling back to provider default key.",
                aiModel.AiProviderId);
            
            if (aiModel.AiProvider == null)
            {
                _logger.LogError(
                    "AiProvider is null for model {ModelName} (ID: {ModelId}). This indicates a data integrity issue.",
                    aiModel.Name, aiModel.Id);
                throw new InvalidOperationException(
                    $"AI Provider is null for model {aiModel.Name}. This indicates a data integrity issue. Please check the database.");
            }
            
            apiKeySecretToUse = aiModel.AiProvider.DefaultApiKey;
            if (!string.IsNullOrEmpty(apiKeySecretToUse))
            {
                _logger.LogInformation("Using default API key for provider {ProviderId}", aiModel.AiProviderId);
            }
        }

        if (string.IsNullOrEmpty(apiKeySecretToUse) && aiModel.ModelType != ModelType.Imagen &&
            aiModel.ModelType != ModelType.AimlFlux)
        {
            var providerName = aiModel.AiProvider?.Name ?? "Unknown";
            _logger.LogError(
                "No API key available for provider {ProviderName} (ID: {ProviderId}) and model {ModelName}. Neither managed nor default key found.",
                providerName, aiModel.AiProviderId, aiModel.Name);
            throw new Exception(
                $"No API keys available for provider {providerName}. Please configure a managed key or a default key for the provider.");
        }

        IAiModelService serviceInstance = InstantiateServiceModel(aiModel, apiKeySecretToUse);

        if (managedApiKey != null)
        {
            return new AiServiceContext(serviceInstance, managedApiKey);
        }

        if (!string.IsNullOrEmpty(apiKeySecretToUse) || aiModel.ModelType == ModelType.Imagen ||
            aiModel.ModelType == ModelType.AimlFlux)
        {
            return new AiServiceContext(serviceInstance, apiKeySecretToUse);
        }

        _logger.LogError(
            "Failed to create AiServiceContext for model {ModelId}, no valid API key or configuration found.", modelId);
        return null;
    }

    private IAiModelService InstantiateServiceModel(AiModel aiModel, string? apiKeySecret)
    {
        var openAiLogger = _serviceProvider.GetRequiredService<ILogger<OpenAiService>>();
        var anthropicLogger = _serviceProvider.GetRequiredService<ILogger<AnthropicService>>();
        var deepSeekLogger = _serviceProvider.GetRequiredService<ILogger<DeepSeekService>>();
        var geminiLogger = _serviceProvider.GetRequiredService<ILogger<GeminiService>>();
        var aimlLogger = _serviceProvider.GetRequiredService<ILogger<AimlApiService>>();
        var imagenLogger = _serviceProvider.GetRequiredService<ILogger<ImagenService>>();
        var grokLogger = _serviceProvider.GetRequiredService<ILogger<GrokService>>();
        var qwenLogger = _serviceProvider.GetRequiredService<ILogger<QwenService>>();

        var resilienceService = _serviceProvider.GetRequiredService<IResilienceService>();
        var httpClient = _httpClientFactory.CreateClient();
        
        if (aiModel.ModelType == ModelType.OpenAiDeepResearch)
        {
            httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        return aiModel.ModelType switch
        {
            ModelType.OpenAi => new OpenAiService(httpClient, apiKeySecret, aiModel.ModelCode, openAiLogger, resilienceService, new OpenAiStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<OpenAiStreamChunkParser>>())),
            ModelType.OpenAiDeepResearch => new OpenAiService(httpClient, apiKeySecret, aiModel.ModelCode, openAiLogger, resilienceService, new OpenAiStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<OpenAiStreamChunkParser>>()), TimeSpan.FromMinutes(10)),
            ModelType.Anthropic => new AnthropicService(httpClient, apiKeySecret, aiModel.ModelCode, anthropicLogger, resilienceService, new AnthropicStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<AnthropicStreamChunkParser>>())),
            ModelType.DeepSeek => new DeepSeekService(httpClient, apiKeySecret, aiModel.ModelCode, deepSeekLogger, resilienceService, new DeepseekStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<DeepseekStreamChunkParser>>())),
            ModelType.Gemini => new GeminiService(httpClient, apiKeySecret, aiModel.ModelCode, geminiLogger, resilienceService, new GeminiStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<GeminiStreamChunkParser>>())),
            ModelType.AimlFlux => new AimlApiService(httpClient, apiKeySecret, aiModel.ModelCode, aimlLogger, resilienceService),
            ModelType.Imagen => new ImagenService(
                httpClient,
                _configuration["AI:Imagen:ProjectId"] ?? throw new InvalidOperationException("Imagen ProjectId not configured."),
                _configuration["AI:Imagen:Region"] ?? throw new InvalidOperationException("Imagen Region not configured."),
                aiModel.ModelCode,
                imagenLogger,
                resilienceService,
                _configuration),
            ModelType.Grok => new GrokService(httpClient, apiKeySecret, aiModel.ModelCode, grokLogger, resilienceService, new GrokStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<GrokStreamChunkParser>>())),
            ModelType.Qwen => new QwenService(httpClient, apiKeySecret, aiModel.ModelCode, qwenLogger, resilienceService, new QwenStreamChunkParser(_serviceProvider.GetRequiredService<ILogger<QwenStreamChunkParser>>())),
            _ => throw new NotSupportedException($"Model type {aiModel.ModelType} not supported for instantiation with IResilienceService in factory.")
        };
    }
}