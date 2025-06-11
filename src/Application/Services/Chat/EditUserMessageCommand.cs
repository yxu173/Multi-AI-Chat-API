using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI.Interfaces;
using Application.Services.Messaging;
using Application.Services.Utilities;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.Chat;

/// <summary>
/// Handles editing an existing user message, cleaning up subsequent AI responses, and streaming a fresh AI response.
/// </summary>
public sealed class EditUserMessageCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IAiRequestOrchestrator _aiRequestOrchestrator;
    private readonly ILogger<EditUserMessageCommand> _logger;

    public EditUserMessageCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IFileAttachmentRepository fileAttachmentRepository,
        IAiRequestOrchestrator aiRequestOrchestrator,
        ILogger<EditUserMessageCommand> logger)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _aiRequestOrchestrator = aiRequestOrchestrator ?? throw new ArgumentNullException(nameof(aiRequestOrchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        Guid chatSessionId,
        Guid userId,
        Guid messageId,
        string newContent,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        string? safetyTolerance = null,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var messageToEdit = chatSession.Messages.FirstOrDefault(m => m.Id == messageId && m.UserId == userId && !m.IsFromAi);
        if (messageToEdit == null)
            throw new Exception("Message not found or you do not have permission to edit it.");

        var contentToUse = newContent;

        var fileAttachments = messageToEdit.FileAttachments?.ToList() ?? new List<FileAttachment>();
        List<Guid> newFileAttachmentIds = Utilities.Utilities.ExtractFileAttachmentIds(contentToUse);
        if (newFileAttachmentIds.Any())
        {
            var newFileAttachments = new List<FileAttachment>();
            foreach (var fileId in newFileAttachmentIds)
            {
                var attachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
                if (attachment != null) newFileAttachments.Add(attachment);
            }
            fileAttachments = newFileAttachments;
        }

        await _messageService.UpdateMessageContentAsync(messageToEdit, contentToUse, fileAttachments, cancellationToken);
        await new MessageEditedNotification(chatSessionId, messageId, contentToUse).PublishAsync(cancellation: cancellationToken);

        var subsequentAiMessages = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > messageToEdit.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        foreach (var subsequentAiMessage in subsequentAiMessages)
        {
            chatSession.RemoveMessage(subsequentAiMessage);
            await _messageService.DeleteMessageAsync(subsequentAiMessage.Id, cancellationToken);
        }

        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);

        try
        {
            var request = new AiOrchestrationRequest(
                ChatSessionId: chatSessionId,
                UserId: userId,
                AiMessageId: aiMessage.Id,
                EnableThinking: false, // Thinking is not enabled for edits
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
            _logger.LogError(ex, "Failed to process AI request after editing message for chat {ChatSessionId}. Failing message.", chatSessionId);
            await _messageService.FailMessageAsync(aiMessage, ex.Message, CancellationToken.None);
            throw;
        }
    }
}
