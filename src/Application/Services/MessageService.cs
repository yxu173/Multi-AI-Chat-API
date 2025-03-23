using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

namespace Application.Services;

public class MessageService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IMediator _mediator;

    public MessageService(IMessageRepository messageRepository, IMediator mediator)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task<Message> CreateAndSaveUserMessageAsync(Guid userId, Guid chatSessionId, string content,
        CancellationToken cancellationToken = default)
    {
        var message = Message.CreateUserMessage(userId, chatSessionId, content);
        await _messageRepository.AddAsync(message, cancellationToken);
        await _mediator.Publish(
            new MessageSentNotification(chatSessionId, new MessageDto(message.Content, false, message.Id)),
            cancellationToken);
        return message;
    }

    public async Task<Message> CreateAndSaveAiMessageAsync(Guid userId, Guid chatSessionId,
        CancellationToken cancellationToken = default)
    {
        var message = Message.CreateAiMessage(userId, chatSessionId);
        await _messageRepository.AddAsync(message, cancellationToken);
        await _mediator.Publish(
            new MessageSentNotification(chatSessionId, new MessageDto(message.Content, true, message.Id)),
            cancellationToken);
        return message;
    }

    public async Task UpdateMessageContentAsync(Message message, string newContent,
        CancellationToken cancellationToken = default)
    {
        message.UpdateContent(newContent);
        await _messageRepository.UpdateAsync(message, cancellationToken);
    }

    public async Task DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _messageRepository.DeleteAsync(messageId, cancellationToken);
    }

    public async Task<Message?> GetLastUserMessageAsync(ChatSession chatSession, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return chatSession.Messages
            .Where(m => m.UserId == userId && !m.IsFromAi)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();
    }
}