using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

public class AiModelServiceFactory : IAiModelServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AiModelServiceFactory(IServiceProvider serviceProvider, IApplicationDbContext dbContext, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public IAiModelService GetService(Guid userId, Guid modelId, string? customApiKey = null, Guid? aiAgentId = null)
    {
        return GetServiceAsync(userId, modelId, customApiKey, aiAgentId).GetAwaiter().GetResult();
    }

    private async Task<IAiModelService> GetServiceAsync(Guid userId, Guid modelId, string? customApiKey, Guid? aiAgentId)
    {
        var aiModel = await _dbContext.AiModels
                          .Include(m => m.AiProvider)
                          .FirstOrDefaultAsync(m => m.Id == modelId) 
                      ?? throw new NotSupportedException($"No AI Model or Provider configured with ID: {modelId}");
        
        var apiKey = await GetApiKeyAsync(userId, aiModel.AiProviderId, customApiKey);
        
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        return aiModel.ModelType switch
        {
            ModelType.OpenAi => new OpenAiService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.Anthropic => new AnthropicService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.DeepSeek => new DeepSeekService(httpClientFactory, apiKey, aiModel.ModelCode),
            ModelType.Gemini => new GeminiService(httpClientFactory, apiKey, aiModel.ModelCode),
            _ => throw new NotSupportedException($"Model type {aiModel.ModelType} not supported.")
        };
    }

    private async Task<string> GetApiKeyAsync(Guid userId, Guid providerId, string? customApiKey)
    {
        if (!string.IsNullOrEmpty(customApiKey)) return customApiKey;
        
        var userApiKey = await _dbContext.UserApiKeys
            .FirstOrDefaultAsync(k => k.UserId == userId && k.AiProviderId == providerId);
            
        if (userApiKey != null)
        {
            userApiKey.UpdateLastUsed();
            await _dbContext.SaveChangesAsync();
            return userApiKey.ApiKey;
        }

        var provider = await _dbContext.AiProviders.FindAsync(providerId) 
            ?? throw new Exception($"AI Provider with ID {providerId} not found.");
            
        return provider.DefaultApiKey 
            ?? throw new Exception($"No API key configured for user {userId} or provider {provider.Name}, and no default key available.");
    }
}