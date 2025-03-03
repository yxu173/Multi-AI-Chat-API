using System;
using System.Linq;
using System.Threading.Tasks;
using Application.Abstractions.Authentication;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Configuration;
using Application.Abstractions.Data;

namespace Infrastructure.Services;

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

    public async Task<IAiModelService> GetServiceAsync(Guid modelId, string customApiKey = null)
    {
        var aiModel = await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .FirstOrDefaultAsync(m => m.Id == modelId);

        if (aiModel == null || aiModel.AiProvider == null)
        {
            throw new NotSupportedException($"No AI Model or Provider configured with ID: {modelId}");
        }
        
        // Determine which API key to use (priority: custom > user > default)
        string apiKey = customApiKey;
        
        if (string.IsNullOrEmpty(apiKey))
        {
            // Try to get user's API key for this provider
            var userId = _userContext.UserId;
            var userApiKey = await _dbContext.UserApiKeys
                .FirstOrDefaultAsync(k => k.UserId == userId && k.AiProviderId == aiModel.AiProviderId);
                
            if (userApiKey != null)
            {
                apiKey = userApiKey.ApiKey;
                // Update last used timestamp
                userApiKey.UpdateLastUsed();
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                // Fall back to default API key
                apiKey = aiModel.AiProvider.DefaultApiKey;
            }
        }
        
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"No API key available for provider: {aiModel.AiProvider.Name}");
        }

        return aiModel.ModelType switch
        {
            ModelType.ChatGPT => CreateChatGptService(aiModel, apiKey),
            ModelType.Claude => CreateClaudeService(aiModel, apiKey),
            ModelType.DeepSeek => CreateDeepSeekService(aiModel, apiKey),
            ModelType.Gemini => CreateGeminiService(aiModel, apiKey),
            ModelType.Imagen3 => CreateImagen3Service(aiModel, apiKey),
            _ => throw new NotSupportedException($"Model type {aiModel.ModelType} not supported.")
        };
    }

    // For backward compatibility, provide a synchronous version that calls the async method
    public IAiModelService GetService(Guid modelId, string customApiKey = null)
    {
        return GetServiceAsync(modelId, customApiKey).GetAwaiter().GetResult();
    }

    private ChatGptService CreateChatGptService(AiModel aiModel, string apiKey)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new ChatGptService(
            httpClientFactory, 
            apiKey, 
            aiModel.InputTokenPricePer1K, 
            aiModel.OutputTokenPricePer1K, 
            aiModel.ModelCode);
    }

    private ClaudeService CreateClaudeService(AiModel aiModel, string apiKey)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new ClaudeService(
            httpClientFactory, 
            apiKey, 
            aiModel.InputTokenPricePer1K, 
            aiModel.OutputTokenPricePer1K);
    }

    private DeepSeekService CreateDeepSeekService(AiModel aiModel, string apiKey)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new DeepSeekService(
            httpClientFactory, 
            apiKey, 
            aiModel.InputTokenPricePer1K, 
            aiModel.OutputTokenPricePer1K);
    }

    private GeminiService CreateGeminiService(AiModel aiModel, string apiKey)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new GeminiService(
            httpClientFactory, 
            apiKey, 
            aiModel.InputTokenPricePer1K, 
            aiModel.OutputTokenPricePer1K);
    }

    private Imagen3Service CreateImagen3Service(AiModel aiModel, string apiKey)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // For Imagen3, we need to get additional configuration
        var projectId = _configuration["AI:Imagen3:ProjectId"];
        var region = _configuration["AI:Imagen3:Region"];
        var publisher = _configuration["AI:Imagen3:Publisher"];
        
        if (string.IsNullOrEmpty(projectId))
            throw new InvalidOperationException("Missing configuration: AI:Imagen3:ProjectId");
            
        if (string.IsNullOrEmpty(region))
            throw new InvalidOperationException("Missing configuration: AI:Imagen3:Region");
            
        if (string.IsNullOrEmpty(publisher))
            throw new InvalidOperationException("Missing configuration: AI:Imagen3:Publisher");
        
        return new Imagen3Service(
            httpClientFactory, 
            apiKey,
            projectId,
            region,
            publisher,
            aiModel.ModelCode);
    }
}