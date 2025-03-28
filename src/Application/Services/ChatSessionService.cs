using Application.Notifications;
using Domain.Repositories;
using MediatR;

namespace Application.Services;

public class ChatSessionService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMediator _mediator;
    private const int MaxTitleLength = 50;

    public ChatSessionService(IChatSessionRepository chatSessionRepository, IMediator mediator)
    {
        _chatSessionRepository =
            chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task<ChatSession> GetChatSessionAsync(Guid chatSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _chatSessionRepository.GetByIdWithModelAsync(chatSessionId)
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
            await _mediator.Publish(new ChatTitleUpdatedNotification(chatSession.Id, title), cancellationToken);
        }
    }

    private string GenerateTitleFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "New Chat";
        return content.Length <= MaxTitleLength ? content : content.Substring(0, MaxTitleLength) + "...";
    }
}