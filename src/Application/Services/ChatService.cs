using System;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using MediatR;
using Application.Notifications;
using Domain.Repositories;
using System.Linq;

namespace Application.Services;

public record MessageDto(string Content, bool IsFromAi, Guid MessageId);

public class ChatService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMediator _mediator;

    public ChatService(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IAiModelServiceFactory aiModelServiceFactory,
        IMediator mediator)
    {
        _chatSessionRepository =
            chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _aiModelServiceFactory =
            aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    //TODO: Make Result Errors 
    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content)
    {
        var chatSession = await _chatSessionRepository.GetByIdAsync(chatSessionId);
        if (chatSession == null) throw new Exception("Chat session not found.");

        
        if (!chatSession.Messages.Any())
        {
            var title = GenerateTitleFromContent(content);
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession);
            
            
            await _mediator.Publish(new ChatTitleUpdatedNotification(chatSessionId, title));
        }

        var userMessage = Message.CreateUserMessage(userId, chatSessionId, content);
        await _messageRepository.AddAsync(userMessage);
        chatSession.AddMessage(userMessage);

        await _mediator.Publish(new MessageSentNotification(
            chatSessionId,
            new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id)
        ));

        var aiMessage = Message.CreateAiMessage(userId, chatSessionId);
        await _messageRepository.AddAsync(aiMessage);
        chatSession.AddMessage(aiMessage);

        await _mediator.Publish(new MessageSentNotification(
            chatSessionId,
            new MessageDto(aiMessage.Content, aiMessage.IsFromAi, aiMessage.Id)
        ));

        var aiService = _aiModelServiceFactory.GetService(chatSession.ModelType);

        try
        {
            var messages = chatSession.Messages
                .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id))
                .ToList();

            await foreach (var chunk in aiService.StreamResponseAsync(messages))
            {
                aiMessage.AppendContent(chunk);
                await _messageRepository.UpdateAsync(aiMessage);
                await _mediator.Publish(new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, chunk));
            }

            aiMessage.CompleteMessage();
        }
        catch (Exception)
        {
            aiMessage.FailMessage();
            throw;
        }
        finally
        {
            await _messageRepository.UpdateAsync(aiMessage);
        }
    }

    public async Task UpdateChatTitleFromFirstMessage(Guid chatSessionId, string content)
    {
        var chatSession = await _chatSessionRepository.GetByIdAsync(chatSessionId);
        if (chatSession == null) throw new Exception("Chat session not found.");

        if (chatSession.Title == "New Chat") // Only update if it's the default title
        {
            var title = GenerateTitleFromContent(content);
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession);
        }
    }

    private string GenerateTitleFromContent(string content)
    {
        // Take first sentence or first 50 characters
        var title = content
            .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        if (string.IsNullOrEmpty(title))
        {
            title = content;
        }

        // Truncate and add ellipsis if too long
        if (title.Length > 50)
        {
            title = title.Substring(0, 47) + "...";
        }

        return title;
    }
}