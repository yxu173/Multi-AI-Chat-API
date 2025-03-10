using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

namespace Application.Services;

public record MessageDto(string Content, bool IsFromAi, Guid MessageId);

public class ChatService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMediator _mediator;
  //  private readonly IChatTokenUsageRepository _tokenUsageRepository;

    public ChatService(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IAiModelServiceFactory aiModelServiceFactory,
        IMediator mediator
        //IChatTokenUsageRepository tokenUsageRepository
        )
    {
        _chatSessionRepository = chatSessionRepository;
        _messageRepository = messageRepository;
        _aiModelServiceFactory = aiModelServiceFactory;
        _mediator = mediator;
//        _tokenUsageRepository = tokenUsageRepository;
    }

    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithModelAsync(chatSessionId);
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

        var aiService =
            _aiModelServiceFactory.GetService(chatSession.UserId, chatSession.AiModelId, chatSession.CustomApiKey);

        var messages = chatSession.Messages
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id))
            .ToList();

        var responseContent = new StringBuilder();

        await foreach (var chunk in aiService.StreamResponseAsync(messages))
        {
            responseContent.Append(chunk);
            aiMessage.AppendContent(chunk);
            await _messageRepository.UpdateAsync(aiMessage);
            await _mediator.Publish(new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, chunk));
        }

        aiMessage.CompleteMessage();
        await _messageRepository.UpdateAsync(aiMessage);

        //  var tokenUsage = await aiService.CountTokensAsync(messages);

        // var chatTokenUsage = ChatTokenUsage.Create(
        //     aiMessage.Id,
        //     tokenUsage.InputTokens,
        //     tokenUsage.OutputTokens,
        //     tokenUsage.TotalCost);
        //
        // await _tokenUsageRepository.AddAsync(chatTokenUsage);
    }

    private string GenerateTitleFromContent(string content)
    {
        const int maxTitleLength = 50;

        if (string.IsNullOrWhiteSpace(content))
            return "New Chat";

        var title = content.Length <= maxTitleLength
            ? content
            : content.Substring(0, maxTitleLength) + "...";

        return title;
    }
}