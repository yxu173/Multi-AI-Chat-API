using Application.Abstractions.Messaging;
using Application.Exceptions;
using Application.Notifications;
using Application.Services.AI;
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
using Hangfire;
using Application.Features.Chats.GenerateTitle;
using Application.Features.Chats.SummarizeHistory;

namespace Application.Features.Chats.SendMessage;

public class SendMessageHandler : Abstractions.Messaging.ICommandHandler<SendMessageCommand>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IStreamingService _streamingService;
    private readonly ILogger<SendMessageHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;

    public SendMessageHandler(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IStreamingService streamingService,
        ILogger<SendMessageHandler> logger,
        IBackgroundJobClient backgroundJobClient,
        IFileAttachmentRepository fileAttachmentRepository)
    {
        _chatSessionRepository =
            chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _streamingService = streamingService ?? throw new ArgumentNullException(nameof(streamingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
        _fileAttachmentRepository = fileAttachmentRepository ??
                                    throw new ArgumentNullException(nameof(fileAttachmentRepository));
    }

    public async Task<Result> ExecuteAsync(SendMessageCommand command, CancellationToken ct)
    {
        var allAttachmentIds = new List<Guid>();
        if (command.FileAttachmentIds != null)
            allAttachmentIds.AddRange(command.FileAttachmentIds);
        if (command.ImageAttachmentIds != null)
            allAttachmentIds.AddRange(command.ImageAttachmentIds);
        List<FileAttachment>? allAttachments = null;
        if (allAttachmentIds.Count > 0)
        {
            allAttachments = new List<FileAttachment>();
            foreach (var id in allAttachmentIds)
            {
                var attachment = await _fileAttachmentRepository.GetByIdAsync(id, ct);
                if (attachment != null)
                    allAttachments.Add(attachment);
            }
        }

        await CreateAndSaveUserMessageAsync(
            command.UserId,
            command.ChatSessionId,
            command.Content,
            allAttachments,
            cancellationToken: ct);


        var aiMessage = await CreateAndSaveAiMessageAsync(command.UserId, command.ChatSessionId, ct);

        try
        {
            var streamingRequest = new StreamingRequest(
                ChatSessionId: command.ChatSessionId,
                UserId: command.UserId,
                AiMessageId: aiMessage.Id,
                EnableThinking: command.EnableThinking,
                ImageSize: command.ImageSize,
                NumImages: command.NumImages,
                OutputFormat: command.OutputFormat,
                EnableDeepSearch: command.EnableDeepSearch,
                Temperature: command.Temperature,
                OutputToken: command.OutputToken
            );

            await _streamingService.StreamResponseAsync(streamingRequest, ct);

            var currentChat = await _chatSessionRepository.GetByIdAsync(command.ChatSessionId);

            if (currentChat!.Messages.Count == 2 && currentChat.Title == "New Chat")
            {
                _backgroundJobClient.Enqueue<GenerateChatTitleJob>(j =>
                    j.GenerateAsync(command.ChatSessionId, CancellationToken.None));
                _logger.LogInformation("Enqueued title generation job for chat {ChatSessionId}", command.ChatSessionId);
            }

            const int summarizationMessageThreshold = 10;
            if (currentChat.Messages.Count >= summarizationMessageThreshold)
            {
                var messagesSinceLastSummary = currentChat.LastSummarizedAt.HasValue
                    ? currentChat.Messages.Count(m => m.CreatedAt > currentChat.LastSummarizedAt.Value)
                    : currentChat.Messages.Count;

                if (messagesSinceLastSummary >= summarizationMessageThreshold / 2)
                {
                    _backgroundJobClient.Enqueue<SummarizeChatHistoryJob>(j =>
                        j.SummarizeAsync(command.ChatSessionId, CancellationToken.None));
                    _logger.LogInformation("Enqueued history summarization job for chat {ChatSessionId}",
                        command.ChatSessionId);
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AI request for chat {ChatSessionId}. Failing message.",
                command.ChatSessionId);
            await FailMessageAsync(aiMessage, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task CreateAndSaveUserMessageAsync(
        Guid userId,
        Guid chatSessionId,
        string content,
        IEnumerable<FileAttachment>? fileAttachments = null,
        CancellationToken cancellationToken = default)
    {
        var message = Message.CreateUserMessage(userId, chatSessionId, content);

        await _messageRepository.AddAsync(message, cancellationToken);

        if (fileAttachments != null)
        {
            foreach (var attachment in fileAttachments)
            {
                attachment.SetMessageId(message.Id);
                await _fileAttachmentRepository.UpdateAsync(attachment, cancellationToken);
            }
        }

        var messageDto = MessageDto.FromEntity(message);
        await new MessageSentNotification(chatSessionId, messageDto).PublishAsync(cancellation: cancellationToken);
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

    private async Task FailMessageAsync(Message message, string failureReason,
        CancellationToken cancellationToken = default)
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
        await new MessageUpdateNotification(message.ChatSessionId, messageDto).PublishAsync(
            cancellation: cancellationToken);
    }
}