using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.AiProvidersServices;

public class AiModelServiceFactory : IAiModelServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationDbContext _dbContext;
    private readonly IUserContext _userContext;
    private readonly IConfiguration _configuration;

    public AiModelServiceFactory(
        IServiceProvider serviceProvider,
        IApplicationDbContext dbContext,
        IUserContext userContext,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<IAiModelService> GetServiceAsync(Guid userId, Guid modelId, string customApiKey = null)
    {
        var aiModel = await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .FirstOrDefaultAsync(m => m.Id == modelId);

        if (aiModel == null || aiModel.AiProvider == null)
        {
            throw new NotSupportedException($"No AI Model or Provider configured with ID: {modelId}");
        }

        string apiKey = customApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            var userApiKey = await _dbContext.UserApiKeys
                .FirstOrDefaultAsync(k => k.UserId == userId && k.AiProviderId == aiModel.AiProviderId);

            if (userApiKey != null)
            {
                apiKey = userApiKey.ApiKey;
                userApiKey.UpdateLastUsed();
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                apiKey = aiModel.AiProvider.DefaultApiKey;
            }
        }

        // Get user model settings if available
        var userSettings = await _dbContext.UserAiModelSettings
            .FirstOrDefaultAsync(s => s.UserId == userId);

        return aiModel.ModelType switch
        {
            ModelType.OpenAi => CreateChatGptService(aiModel, apiKey, userSettings),
            ModelType.Anthropic => CreateClaudeService(aiModel, apiKey, userSettings),
            ModelType.DeepSeek => CreateDeepSeekService(aiModel, apiKey, userSettings),
            ModelType.Gemini => CreateGeminiService(aiModel, apiKey, userSettings),
            //ModelType.Imagen3 => CreateImagen3Service(aiModel),
            _ => throw new NotSupportedException($"Model type {aiModel.ModelType} not supported.")
        };
    }

    public IAiModelService GetService(Guid userId, Guid modelId, string customApiKey = null)
    {
        return GetServiceAsync(userId, modelId, customApiKey).GetAwaiter().GetResult();
    }

    private OpenAiService CreateChatGptService(AiModel aiModel, string apiKey, UserAiModelSettings? userSettings = null)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new OpenAiService(
            httpClientFactory,
            apiKey,
            aiModel.ModelCode,
            userSettings,
            aiModel);
    }

    private AnthropicService CreateClaudeService(AiModel aiModel, string apiKey,
        UserAiModelSettings? userSettings = null)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new AnthropicService(
            httpClientFactory,
            apiKey,
            aiModel.ModelCode,
            userSettings,
            aiModel);
    }

    private DeepSeekService CreateDeepSeekService(AiModel aiModel, string apiKey,
        UserAiModelSettings? userSettings = null)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new DeepSeekService(
            httpClientFactory,
            apiKey,
            aiModel.ModelCode,
            userSettings);
    }

    private GeminiService CreateGeminiService(AiModel aiModel, string apiKey, UserAiModelSettings? userSettings = null)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new GeminiService(
            httpClientFactory,
            apiKey,
            aiModel.ModelCode,
            userSettings);
    }

    private ImagenService CreateImagen3Service(AiModel aiModel)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();

        var projectId = _configuration["AI:Imagen3:ProjectId"];
        var region = _configuration["AI:Imagen3:Region"];
        var publisher = _configuration["AI:Imagen3:Publisher"];

        if (string.IsNullOrEmpty(projectId))
            throw new InvalidOperationException("Missing configuration: AI:Imagen3:ProjectId");

        if (string.IsNullOrEmpty(region))
            throw new InvalidOperationException("Missing configuration: AI:Imagen3:Region");

        if (string.IsNullOrEmpty(publisher))
            throw new InvalidOperationException("Missing configuration: AI:Imagen3:Publisher");

        return new ImagenService(
            httpClientFactory,
            projectId,
            region,
            publisher,
            aiModel.ModelCode);
    }
}