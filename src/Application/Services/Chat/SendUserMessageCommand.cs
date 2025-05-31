using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Application.Services.AI;
using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
// For ProviderRateLimitException, QuotaExceededException
// For AiAgent, UserAiModelSettings
// For ILogger
// For HttpRequestException

namespace Application.Services.Chat;

/// <summary>
/// Handles the end-to-end flow of sending a new user message and streaming the AI response.
/// </summary>
public sealed class SendUserMessageCommand : BaseAiChatCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IMessageStreamer _messageStreamer;
    private readonly IProviderKeyManagementService _providerKeyManagementService;
    private readonly ILogger<SendUserMessageCommand> _logger;

    private const int MaxRetries = 3;
    private readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2); // Initial delay
    private const double RetryBackoffFactor = 2.0; // Factor to multiply delay by each time

    public SendUserMessageCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IMessageStreamer messageStreamer,
        IAiModelServiceFactory aiModelServiceFactory,
        IProviderKeyManagementService providerKeyManagementService,
        IAiAgentRepository aiAgentRepository,
        IUserAiModelSettingsRepository userAiModelSettingsRepository,
        ILogger<SendUserMessageCommand> logger)
        : base(aiModelServiceFactory, aiAgentRepository, userAiModelSettingsRepository)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
        _providerKeyManagementService = providerKeyManagementService ?? throw new ArgumentNullException(nameof(providerKeyManagementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        Guid chatSessionId,
        Guid userId,
        string content,
        bool enableThinking = false,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        string? safetyTolerance = null,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);

        var userMessage = await _messageService.CreateAndSaveUserMessageAsync(
            userId,
            chatSessionId,
            content,
            fileAttachments: null,
            cancellationToken);

        await _chatSessionService.UpdateChatSessionTitleAsync(chatSession, content, cancellationToken);

        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(aiMessage);

        var (aiService, apiKey, aiAgent) = await base.PrepareForAiInteractionAsync(userId, chatSession, cancellationToken);
        var userSettings = await UserAiModelSettingsRepository.GetDefaultByUserIdAsync(userId, cancellationToken);

        var requestContext = new AiRequestContext(
            UserId: userId,
            ChatSession: chatSession,
            History: new List<MessageDto>(),
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking: enableThinking,
            ImageSize: imageSize,
            NumImages: numImages,
            OutputFormat: outputFormat,
            EnableSafetyChecker: enableSafetyChecker,
            SafetyTolerance: safetyTolerance);
        requestContext = requestContext with { History = HistoryBuilder.BuildHistory(requestContext, aiMessage) };

        bool streamSucceeded = false;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            TimeSpan delayForThisAttempt = TimeSpan.FromSeconds(Math.Pow(RetryBackoffFactor, attempt -1) * InitialRetryDelay.TotalSeconds);

            try
            {
                _logger.LogInformation("Attempt {Attempt}/{MaxRetries} to get AI service for chat {ChatSessionId}", attempt, MaxRetries, chatSessionId);
                var serviceContext = await AiModelServiceFactory.GetServiceContextAsync(userId, chatSession.AiModelId, chatSession.AiAgentId, cancellationToken);

                if (serviceContext?.Service == null)
                {
                    _logger.LogWarning("Failed to get AI service or key on attempt {Attempt} for chat {ChatSessionId}. No key available or service instantiation failed.", attempt, chatSessionId);
                    if (attempt == MaxRetries) throw new Exception("Failed to obtain AI service after multiple retries: No API key available or service setup failed.");
                    _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to service/key acquisition failure.", delayForThisAttempt, chatSessionId);
                    await Task.Delay(delayForThisAttempt, cancellationToken); 
                    continue;
                }

                _logger.LogInformation("Successfully obtained AI service. Attempting to stream response for chat {ChatSessionId}, attempt {Attempt}", chatSessionId, attempt);
                
                await _messageStreamer.StreamResponseAsync(
                    requestContext, 
                    aiMessage, 
                    serviceContext.Service, 
                    cancellationToken, 
                    serviceContext.ApiKey?.Id);
                
                streamSucceeded = true;
                if (serviceContext.ApiKey != null) // Only report success for managed keys
                {
                    await _providerKeyManagementService.ReportKeySuccessAsync(serviceContext.ApiKey.Id, CancellationToken.None); 
                }
                _logger.LogInformation("Successfully streamed AI response for chat {ChatSessionId} on attempt {Attempt}", chatSessionId, attempt);
                break; // Success, exit retry loop
            }
            catch (ProviderRateLimitException ex)
            {
                _logger.LogWarning(ex, "Provider rate limit hit on attempt {Attempt} for chat {ChatSessionId}. Key ID: {ApiKeyIdUsed}", attempt, chatSessionId, ex.ApiKeyIdUsed);
                if (ex.ApiKeyIdUsed.HasValue)
                {
                    await _providerKeyManagementService.ReportKeyRateLimitedAsync(ex.ApiKeyIdUsed.Value, ex.RetryAfter ?? delayForThisAttempt, CancellationToken.None);
                }
                if (attempt == MaxRetries) throw; // Rethrow if max retries reached
                var actualDelay = ex.RetryAfter ?? delayForThisAttempt;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to provider rate limit.", actualDelay, chatSessionId);
                await Task.Delay(actualDelay, cancellationToken); // Wait before retrying
            }
            catch (HttpRequestException ex) // Includes other HTTP errors from BaseAiService
            {
                _logger.LogError(ex, "HTTP request exception on attempt {Attempt} for chat {ChatSessionId}", attempt, chatSessionId);
                if (attempt == MaxRetries) throw; 
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to HTTP request exception.", delayForThisAttempt, chatSessionId);
                await Task.Delay(delayForThisAttempt, cancellationToken); // Basic retry for general HTTP issues
            }
            catch (QuotaExceededException ex) // This is from user's subscription, not API key rate limit
            {
                _logger.LogWarning(ex, "User quota exceeded for chat {ChatSessionId}. No further retries.", chatSessionId);
                throw; // Rethrow immediately, no point in retrying API calls
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on attempt {Attempt} during AI interaction for chat {ChatSessionId}", attempt, chatSessionId);
                // For unknown errors, might be safer to not retry or have a more specific retry strategy
                if (attempt == MaxRetries) throw; 
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to unhandled exception.", delayForThisAttempt, chatSessionId);
                await Task.Delay(delayForThisAttempt, cancellationToken); // Basic retry
            }
        }

        if (!streamSucceeded)
        {
            // If loop finished without success, ensure AI message is marked as failed.
            // _messageStreamer.StreamResponseAsync internally calls _aiMessageFinalizer on exceptions.
            // However, if GetServiceContextAsync consistently fails, StreamResponseAsync might not be called.
            // In this case, the aiMessage is created but never processed.
            // Explicitly fail the message if we exited the loop without a successful stream.
            _logger.LogError("Failed to stream AI response for chat {ChatSessionId} after {MaxRetries} attempts.", chatSessionId, MaxRetries);
            await _messageService.FailMessageAsync(aiMessage, "Failed to get response from AI provider after multiple attempts.", CancellationToken.None);            
            throw new Exception($"Failed to get AI response for chat {chatSessionId} after {MaxRetries} attempts. Please try again later.");
        }
    }
}
