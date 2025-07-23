using Application.Notifications;
using Application.Services.AI;
using Application.Services.Messaging;
using Application.Services.Streaming;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using SharedKernal;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Exceptions;

namespace Application.Features.Chats.RegenerateResponse;

public class RegenerateResponseHandler : Application.Abstractions.Messaging.ICommandHandler<RegenerateResponseCommand>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IStreamingService _streamingService;
    private readonly ILogger<RegenerateResponseHandler> _logger;

    public RegenerateResponseHandler(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IStreamingService streamingService,
        ILogger<RegenerateResponseHandler> logger)
    {
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _streamingService = streamingService ?? throw new ArgumentNullException(nameof(streamingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result> ExecuteAsync(RegenerateResponseCommand command, CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(command.ChatSessionId)
                          ?? throw new NotFoundException(nameof(ChatSession), command.ChatSessionId);

        var userMessage = chatSession.Messages.FirstOrDefault(m => m.Id == command.UserMessageId && m.UserId == command.UserId && !m.IsFromAi);
        if (userMessage == null) throw new Exception("Original user message not found or access denied.");
        
        var messagesToDelete = chatSession.Messages
            .Where(m => m.CreatedAt > userMessage.CreatedAt)
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

        var newAiMessage = await CreateAndSaveAiMessageAsync(command.UserId, command.ChatSessionId, cancellationToken);
        history.Add(newAiMessage);
        
        try
        {
            var streamingRequest = new StreamingRequest(
                ChatSessionId: command.ChatSessionId,
                UserId: command.UserId,
                AiMessageId: newAiMessage.Id,
                History: history,
                EnableThinking: false,
                ImageSize: null,
                NumImages: null,
                OutputFormat: null
            );

            await _streamingService.StreamResponseAsync(streamingRequest, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate AI response for chat {ChatSessionId}. Failing message.", command.ChatSessionId);
            await FailMessageAsync(newAiMessage, ex.Message, CancellationToken.None);
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