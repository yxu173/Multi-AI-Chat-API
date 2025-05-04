using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

namespace Application.Services.Messaging;

public class MessageService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IMediator _mediator;

    public MessageService(
        IMessageRepository messageRepository, 
        IFileAttachmentRepository fileAttachmentRepository,
        IMediator mediator)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task<Message> CreateAndSaveUserMessageAsync(
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
        
        var messageDto = new MessageDto(message.Content, false, message.Id)
        {
            FileAttachments = message.FileAttachments.ToList()
        };
        
        await _mediator.Publish(
            new MessageSentNotification(chatSessionId, messageDto),
            cancellationToken);
        return message;
    }

    public async Task<Message> CreateAndSaveAiMessageAsync(
        Guid userId, 
        Guid chatSessionId,
        CancellationToken cancellationToken = default)
    {
        var message = Message.CreateAiMessage(userId, chatSessionId);
        await _messageRepository.AddAsync(message, cancellationToken);
        await _mediator.Publish(
            new MessageSentNotification(chatSessionId, new MessageDto(message.Content, true, message.Id)),
            cancellationToken);
        return message;
    }

    public async Task UpdateMessageContentAsync(
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

    public async Task UpdateMessageThinkingContentAsync(
        Message message,
        string? thinkingContent,
        CancellationToken cancellationToken = default)
    {
        message.UpdateThinkingContent(thinkingContent);
        await _messageRepository.UpdateAsync(message, cancellationToken);
    }

    public async Task DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var attachments = await _fileAttachmentRepository.GetByMessageIdAsync(messageId, cancellationToken);
        foreach (var attachment in attachments)
        {
            await _fileAttachmentRepository.DeleteAsync(attachment.Id, cancellationToken);
        }
        
        await _messageRepository.DeleteAsync(messageId, cancellationToken);
    }
    
    public async Task<IReadOnlyList<FileAttachment>> GetMessageAttachmentsAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        return await _fileAttachmentRepository.GetByMessageIdAsync(messageId, cancellationToken);
    }
}