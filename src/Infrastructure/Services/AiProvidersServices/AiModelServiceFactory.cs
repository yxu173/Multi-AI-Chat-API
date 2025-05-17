using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Domain.Enums;
using Domain.Aggregates.Admin;
using Infrastructure.Services.AiProvidersServices;
using Infrastructure.Services.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Domain.Aggregates.Chats;

public class AiModelServiceFactory : IAiModelServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IProviderKeyManagementService _keyManagementService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiModelServiceFactory> _logger;

    public AiModelServiceFactory(
        IServiceProvider serviceProvider,
        IApplicationDbContext dbContext,
        IConfiguration configuration,
        ISubscriptionService subscriptionService,
        IProviderKeyManagementService keyManagementService,
        IHttpClientFactory httpClientFactory,
        ILogger<AiModelServiceFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        _keyManagementService = keyManagementService ?? throw new ArgumentNullException(nameof(keyManagementService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        var (hasQuota, quotaErrorMessage) =
            await _subscriptionService.CheckUserQuotaAsync(userId, cancellationToken: cancellationToken);
        if (!hasQuota)
        {
            throw new QuotaExceededException(quotaErrorMessage ?? "Quota exceeded for user subscription.");
        }

        var aiModel = await _dbContext.AiModels
                          .Include(m => m.AiProvider)
                          .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken)
                      ?? throw new NotSupportedException($"No AI Model or Provider configured with ID: {modelId}");

        string? apiKeySecretToUse = null;
        ProviderApiKey? managedApiKey = null;

        managedApiKey =
            await _keyManagementService.GetProviderApiKeyObjectAsync(aiModel.AiProviderId, cancellationToken);
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
            apiKeySecretToUse = aiModel.AiProvider.DefaultApiKey;
            if (!string.IsNullOrEmpty(apiKeySecretToUse))
            {
                _logger.LogInformation("Using default API key for provider {ProviderId}", aiModel.AiProviderId);
            }
        }

        if (string.IsNullOrEmpty(apiKeySecretToUse) && aiModel.ModelType != ModelType.Imagen &&
            aiModel.ModelType != ModelType.AimlFlux)
        {
            _logger.LogError(
                "No API key available for provider {ProviderName} (ID: {ProviderId}) and model {ModelName}. Neither managed nor default key found.",
                aiModel.AiProvider.Name, aiModel.AiProviderId, aiModel.Name);
            throw new Exception(
                $"No API keys available for provider {aiModel.AiProvider.Name}. Please configure a managed key or a default key for the provider.");
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
            ModelType.OpenAi => new OpenAiService(_httpClientFactory, apiKeySecret, aiModel.ModelCode, openAiLogger),
            ModelType.Anthropic => new AnthropicService(_httpClientFactory, apiKeySecret, aiModel.ModelCode,
                anthropicLogger),
            ModelType.DeepSeek => new DeepSeekService(_httpClientFactory, apiKeySecret, aiModel.ModelCode,
                deepSeekLogger),
            ModelType.Gemini => new GeminiService(_httpClientFactory, apiKeySecret, aiModel.ModelCode, geminiLogger),
            ModelType.AimlFlux => new AimlApiService(_httpClientFactory, apiKeySecret, aiModel.ModelCode, aimlLogger),
            ModelType.Imagen => CreateImagenService(_httpClientFactory, aiModel.ModelCode, imagenLogger),
            ModelType.Grok => new GrokService(_httpClientFactory, apiKeySecret, aiModel.ModelCode, grokLogger),
            ModelType.Qwen => new QwenService(_httpClientFactory, apiKeySecret, aiModel.ModelCode, qwenLogger),
            _ => throw new NotSupportedException($"Model type {aiModel} not supported.")
        };
    }

    private ImagenService CreateImagenService(IHttpClientFactory httpClientFactory, string modelCode,
        ILogger<ImagenService>? logger)
    {
        var projectId = _configuration["AI:Imagen:ProjectId"] ??
                        throw new InvalidOperationException("Imagen ProjectId not configured.");
        var region = _configuration["AI:Imagen:Region"] ??
                     throw new InvalidOperationException("Imagen Region not configured.");
        return new ImagenService(httpClientFactory, projectId, region, modelCode, logger);
    }
}