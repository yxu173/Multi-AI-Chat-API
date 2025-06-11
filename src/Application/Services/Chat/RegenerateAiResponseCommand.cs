using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.Chat;

/// <summary>
/// Handles deleting previous AI response(s) for a user message and generating a new fresh response.
/// </summary>
public sealed class RegenerateAiResponseCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IAiRequestOrchestrator _aiRequestOrchestrator;
    private readonly ILogger<RegenerateAiResponseCommand> _logger;

    public RegenerateAiResponseCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IAiRequestOrchestrator aiRequestOrchestrator,
        ILogger<RegenerateAiResponseCommand> logger)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _aiRequestOrchestrator = aiRequestOrchestrator ?? throw new ArgumentNullException(nameof(aiRequestOrchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        Guid chatSessionId,
        Guid userId,
        Guid userMessageId,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);

        var userMessage = chatSession.Messages.FirstOrDefault(m => m.Id == userMessageId && m.UserId == userId && !m.IsFromAi);
        if (userMessage == null) throw new Exception("Original user message not found or access denied.");

        var aiMessageToDelete = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > userMessage.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefault();

        if (aiMessageToDelete != null)
        {
            chatSession.RemoveMessage(aiMessageToDelete);
            await _messageService.DeleteMessageAsync(aiMessageToDelete.Id, cancellationToken);
            await new MessageDeletedNotification(chatSessionId, aiMessageToDelete.Id).PublishAsync(cancellation: cancellationToken);
        }

        var newAiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        
        try
        {
            var request = new AiOrchestrationRequest(
                ChatSessionId: chatSessionId,
                UserId: userId,
                AiMessageId: newAiMessage.Id,
                EnableThinking: false,
                ImageSize: null,
                NumImages: null,
                OutputFormat: null,
                EnableSafetyChecker: null,
                SafetyTolerance: null
            );

            await _aiRequestOrchestrator.ProcessRequestAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate AI response for chat {ChatSessionId}. Failing message.", chatSessionId);
            await _messageService.FailMessageAsync(newAiMessage, ex.Message, CancellationToken.None);
            throw;
        }
    }
}
