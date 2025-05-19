using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;

namespace Application.Services.Chat;

public class ChatSessionService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private const int MaxTitleLength = 50;

    public ChatSessionService(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository =
            chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
    }

    public async Task<ChatSession> GetChatSessionAsync(Guid chatSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(chatSessionId)
                      ?? throw new Exception("Chat session not found.");
        return session;
    }

    public async Task UpdateChatSessionTitleAsync(ChatSession chatSession, string content,
        CancellationToken cancellationToken = default)
    {
        if (!chatSession.Messages.Any())
        {
            var title = GenerateTitleFromContent(content);
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession, cancellationToken);
            await new ChatTitleUpdatedNotification(chatSession.Id, title).PublishAsync(cancellation: cancellationToken);
        }
    }

    private string GenerateTitleFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "New Chat";
        return content.Length <= MaxTitleLength ? content : content.Substring(0, MaxTitleLength) + "...";
    }
}