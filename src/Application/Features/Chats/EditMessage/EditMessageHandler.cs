using Application.Abstractions.Messaging;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.Messaging;
using Application.Services.Streaming;
using Application.Services.Utilities;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using SharedKernal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Exceptions;

namespace Application.Features.Chats.EditMessage;

public class EditMessageHandler : Application.Abstractions.Messaging.ICommandHandler<EditMessageCommand>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IStreamingService _streamingService;
    private readonly ILogger<EditMessageHandler> _logger;

    public EditMessageHandler(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IStreamingService streamingService,
        ILogger<EditMessageHandler> logger)
    {
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _streamingService = streamingService ?? throw new ArgumentNullException(nameof(streamingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result> ExecuteAsync(EditMessageCommand command, CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(command.ChatSessionId)
                          ?? throw new NotFoundException(nameof(ChatSession), command.ChatSessionId);
        var messageToEdit = chatSession.Messages.FirstOrDefault(m => m.Id == command.MessageId && m.UserId == command.UserId && !m.IsFromAi);
        if (messageToEdit == null)
            throw new Exception("Message not found or you do not have permission to edit it.");

        var contentToUse = command.NewContent;

        var fileAttachments = messageToEdit.FileAttachments?.ToList() ?? new List<FileAttachment>();
        List<Guid> newFileAttachmentIds = Utilities.ExtractFileAttachmentIds(contentToUse);
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

        await UpdateMessageContentAsync(messageToEdit, contentToUse, fileAttachments, cancellationToken);
        messageToEdit.UpdateContent(contentToUse);

        await new MessageEditedNotification(command.ChatSessionId, command.MessageId, contentToUse).PublishAsync(cancellation: cancellationToken);

        var messagesToDelete = chatSession.Messages
            .Where(m => m.CreatedAt > messageToEdit.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        
        var messageIdsToDelete = messagesToDelete.Select(m => m.Id).ToList();

        if (messageIdsToDelete.Any())
        {
            await _messageRepository.BulkDeleteAsync(chatSession.UserId, messageIdsToDelete, cancellationToken);
            await new MessageDeletedNotification(command.ChatSessionId, messageIdsToDelete.AsReadOnly()).PublishAsync(cancellation: cancellationToken);
        }

        var history = chatSession.Messages
            .Except(messagesToDelete)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var aiMessage = await CreateAndSaveAiMessageAsync(command.UserId, command.ChatSessionId, cancellationToken);
        history.Add(aiMessage);

        try
        {
            var streamingRequest = new StreamingRequest(
                ChatSessionId: command.ChatSessionId,
                UserId: command.UserId,
                AiMessageId: aiMessage.Id,
                History: history,
                EnableThinking: false,
                ImageSize: command.ImageSize,
                NumImages: command.NumImages,
                OutputFormat: command.OutputFormat,
                Temperature: command.Temperature,
                OutputToken: command.OutputToken
            );

            await _streamingService.StreamResponseAsync(streamingRequest, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AI request after editing message for chat {ChatSessionId}. Failing message.", command.ChatSessionId);
            await FailMessageAsync(aiMessage, ex.Message, CancellationToken.None);
            throw;
        }
    }
    
    private async Task<Message> CreateAndSaveAiMessageAsync(
        Guid userId,
        Guid chatSessionId,
        CancellationToken cancellationToken = default)
    {
        var message = Message.CreateAiMessage(userId, chatSessionId);
        await _messageRepository.AddAsync(message, cancellationToken);

        var messageDto = MessageDto.FromEntity(message);
        await new MessageSentNotification(chatSessionId, messageDto).PublishAsync(cancellation: cancellationToken);
        return message;
    }
    
    private async Task UpdateMessageContentAsync(
        Message message,
        string newContent,
        List<FileAttachment>? fileAttachments = null,
        CancellationToken cancellationToken = default)
    {
        message.UpdateContent(newContent);

        if (fileAttachments != null)
        {
            message.ClearFileAttachments();

            foreach (var attachment in fileAttachments)
            {
                message.AddFileAttachment(attachment);
            }
        }

        await _messageRepository.UpdateAsync(message, cancellationToken);
    }
    
    private async Task DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var attachments = await _fileAttachmentRepository.GetByMessageIdAsync(messageId, cancellationToken);
        foreach (var attachment in attachments)
        {
            await _fileAttachmentRepository.DeleteAsync(attachment.Id, cancellationToken);
        }

        await _messageRepository.DeleteAsync(messageId, cancellationToken);
    }
    
    private async Task FailMessageAsync(Message message, string failureReason, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        message.FailMessage();
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            message.UpdateContent($"[Error: {failureReason}]");
        }
        else
        {
            message.UpdateContent($"{message.Content}\n[Error: {failureReason}]");
        }

        await _messageRepository.UpdateAsync(message, cancellationToken);

        var messageDto = MessageDto.FromEntity(message);
        await new MessageUpdateNotification(message.ChatSessionId, messageDto).PublishAsync(cancellation: cancellationToken);
    }
} 