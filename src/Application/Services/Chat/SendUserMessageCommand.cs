using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Application.Services.AI;
using Application.Services.AI.Interfaces;
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
public sealed class SendUserMessageCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IAiRequestOrchestrator _aiRequestOrchestrator;
    private readonly ILogger<SendUserMessageCommand> _logger;

    public SendUserMessageCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IAiRequestOrchestrator aiRequestOrchestrator,
        ILogger<SendUserMessageCommand> logger)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _aiRequestOrchestrator = aiRequestOrchestrator ?? throw new ArgumentNullException(nameof(aiRequestOrchestrator));
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
        
        try
        {
            var request = new AiOrchestrationRequest(
                ChatSessionId: chatSessionId,
                UserId: userId,
                AiMessageId: aiMessage.Id,
                EnableThinking: enableThinking,
                ImageSize: imageSize,
                NumImages: numImages,
                OutputFormat: outputFormat,
                EnableSafetyChecker: enableSafetyChecker,
                SafetyTolerance: safetyTolerance
            );

            await _aiRequestOrchestrator.ProcessRequestAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AI request for chat {ChatSessionId}. Failing message.", chatSessionId);
            await _messageService.FailMessageAsync(aiMessage, ex.Message, CancellationToken.None);
            throw;
        }
    }
}
