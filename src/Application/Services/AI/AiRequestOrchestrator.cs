using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Application.Services.AI.Interfaces;
using Application.Services.Helpers;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.Messaging;

namespace Application.Services.AI;

public class AiRequestOrchestrator : IAiRequestOrchestrator
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMessageStreamer _messageStreamer;
    private readonly IProviderKeyManagementService _providerKeyManagementService;
    private readonly ILogger<AiRequestOrchestrator> _logger;
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;
    private readonly IAiAgentRepository _aiAgentRepository;

    private const int MaxRetries = 3;
    private readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private const double RetryBackoffFactor = 2.0;

    public AiRequestOrchestrator(
        IChatSessionRepository chatSessionRepository,
        ISubscriptionService subscriptionService,
        IAiModelServiceFactory aiModelServiceFactory,
        IMessageStreamer messageStreamer,
        IProviderKeyManagementService providerKeyManagementService,
        ILogger<AiRequestOrchestrator> logger,
        IUserAiModelSettingsRepository userAiModelSettingsRepository,
        IAiAgentRepository aiAgentRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _subscriptionService = subscriptionService;
        _aiModelServiceFactory = aiModelServiceFactory;
        _messageStreamer = messageStreamer;
        _providerKeyManagementService = providerKeyManagementService;
        _logger = logger;
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task ProcessRequestAsync(AiOrchestrationRequest request, CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(request.ChatSessionId)
            ?? throw new NotFoundException(nameof(ChatSession), request.ChatSessionId);

        var aiMessage = chatSession.Messages.FirstOrDefault(m => m.Id == request.AiMessageId)
            ?? throw new NotFoundException(nameof(Message), request.AiMessageId);

        var (hasQuota, errorMessage) = await _subscriptionService.CheckUserQuotaAsync(request.UserId, 1, 0, cancellationToken);
        if (!hasQuota)
        {
            throw new QuotaExceededException(errorMessage ?? "User has exceeded their quota.");
        }

        var userSettings = await _userAiModelSettingsRepository.GetDefaultByUserIdAsync(request.UserId, cancellationToken);
        var aiAgent = chatSession.AiAgentId.HasValue
            ? await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken)
            : null;

        var requestContext = new AiRequestContext(
            UserId: request.UserId,
            ChatSession: chatSession,
            History: HistoryBuilder.BuildHistory(chatSession, aiAgent, userSettings, MessageDto.FromEntity(aiMessage)),
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking: request.EnableThinking,
            ImageSize: request.ImageSize,
            NumImages: request.NumImages,
            OutputFormat: request.OutputFormat,
            EnableSafetyChecker: request.EnableSafetyChecker,
            SafetyTolerance: request.SafetyTolerance);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            TimeSpan delayForThisAttempt = TimeSpan.FromSeconds(Math.Pow(RetryBackoffFactor, attempt - 1) * InitialRetryDelay.TotalSeconds);

            try
            {
                var serviceContext = await _aiModelServiceFactory.GetServiceContextAsync(request.UserId, chatSession.AiModelId, chatSession.AiAgentId, cancellationToken);
                
                await _messageStreamer.StreamResponseAsync(
                    requestContext,
                    aiMessage,
                    serviceContext.Service,
                    cancellationToken,
                    serviceContext.ApiKey?.Id);

                if (serviceContext.ApiKey != null)
                {
                    await _providerKeyManagementService.ReportKeySuccessAsync(serviceContext.ApiKey.Id, CancellationToken.None);
                }

                _logger.LogInformation("Successfully streamed AI response for chat {ChatSessionId} on attempt {Attempt}", request.ChatSessionId, attempt);
                return; // Success
            }
            catch (ProviderRateLimitException ex)
            {
                _logger.LogWarning(ex, "Provider rate limit hit on attempt {Attempt} for chat {ChatSessionId}. Key ID: {ApiKeyIdUsed}", attempt, request.ChatSessionId, ex.ApiKeyIdUsed);
                if (ex.ApiKeyIdUsed.HasValue)
                {
                    await _providerKeyManagementService.ReportKeyRateLimitedAsync(ex.ApiKeyIdUsed.Value, ex.RetryAfter ?? delayForThisAttempt, CancellationToken.None);
                }
                if (attempt == MaxRetries) throw;
                var actualDelay = ex.RetryAfter ?? delayForThisAttempt;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to provider rate limit.", actualDelay, request.ChatSessionId);
                await Task.Delay(actualDelay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request exception on attempt {Attempt} for chat {ChatSessionId}", attempt, request.ChatSessionId);
                if (attempt == MaxRetries) throw;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to HTTP request exception.", delayForThisAttempt, request.ChatSessionId);
                await Task.Delay(delayForThisAttempt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on attempt {Attempt} during AI interaction for chat {ChatSessionId}", attempt, request.ChatSessionId);
                if (attempt == MaxRetries) throw;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to unhandled exception.", delayForThisAttempt, request.ChatSessionId);
                await Task.Delay(delayForThisAttempt, cancellationToken);
            }
        }

        throw new Exception($"Failed to get AI response for chat {request.ChatSessionId} after {MaxRetries} attempts. Please try again later.");
    }
} 