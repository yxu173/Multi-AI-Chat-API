using Application.Abstractions.Messaging;
using Application.Exceptions;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.Messaging;
using Application.Services.Streaming;
using Application.Services.Utilities;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using SharedKernal;
using Domain.Aggregates.Chats;
using Application.Abstractions.Interfaces;
using Domain.Enums;

namespace Application.Features.Chats.SendMessage;

public class SendMessageHandler : Application.Abstractions.Messaging.ICommandHandler<SendMessageCommand>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IStreamingService _streamingService;
    private readonly ILogger<SendMessageHandler> _logger;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly ChatTitleGenerator _titleGenerator;

    public SendMessageHandler(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IStreamingService streamingService,
        ILogger<SendMessageHandler> logger,
        IAiModelServiceFactory aiModelServiceFactory,
        ChatTitleGenerator titleGenerator)
    {
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _streamingService = streamingService ?? throw new ArgumentNullException(nameof(streamingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _titleGenerator = titleGenerator ?? throw new ArgumentNullException(nameof(titleGenerator));
    }

    public async Task<Result> ExecuteAsync(SendMessageCommand command, CancellationToken ct)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(command.ChatSessionId)
                          ?? throw new NotFoundException(nameof(ChatSession), command.ChatSessionId);

        var userMessage = await CreateAndSaveUserMessageAsync(
            command.UserId,
            command.ChatSessionId,
            command.Content,
            fileAttachments: null,
            cancellationToken: ct);

        await UpdateChatSessionTitleAsync(chatSession, command.Content, ct);

        var aiMessage = await CreateAndSaveAiMessageAsync(command.UserId, command.ChatSessionId, ct);
        
        try
        {
            var request = new StreamingRequest(
                ChatSessionId: command.ChatSessionId,
                UserId: command.UserId,
                AiMessageId: aiMessage.Id,
                EnableThinking: command.EnableThinking,
                ImageSize: command.ImageSize,
                NumImages: command.NumImages,
                OutputFormat: command.OutputFormat,
                EnableSafetyChecker: command.EnableSafetyChecker,
                SafetyTolerance: command.SafetyTolerance
            );

            await _streamingService.StreamResponseAsync(request, ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AI request for chat {ChatSessionId}. Failing message.", command.ChatSessionId);
            await FailMessageAsync(aiMessage, ex.Message, CancellationToken.None);
            throw;
        }
    }
    
    private async Task UpdateChatSessionTitleAsync(ChatSession chatSession, string content,
        CancellationToken cancellationToken = default)
    {
        if (chatSession.Messages.Any())
        {
            var title = await _titleGenerator.GenerateTitleAsync(chatSession, content, cancellationToken);
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession, cancellationToken);
            await new ChatTitleUpdatedNotification(chatSession.Id, title).PublishAsync(cancellation: cancellationToken);
        }
    }
    
    private async Task<Message> CreateAndSaveUserMessageAsync(
        Guid userId,
        Guid chatSessionId,
        string content,
        IEnumerable<FileAttachment>? fileAttachments = null,
        CancellationToken cancellationToken = default)
    {
        var message = Message.CreateUserMessage(userId, chatSessionId, content);

        if (fileAttachments != null)
        {
            foreach (var attachment in fileAttachments)
            {
                message.AddFileAttachment(attachment);
            }
        }

        await _messageRepository.AddAsync(message, cancellationToken);

        var messageDto = MessageDto.FromEntity(message);

        await new MessageSentNotification(chatSessionId, messageDto).PublishAsync(cancellation: cancellationToken);
        return message;
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