using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.UpdateChatSession;

public class UpdateChatSessionCommandHandler : ICommandHandler<UpdateChatSessionCommand, bool>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public UpdateChatSessionCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> Handle(UpdateChatSessionCommand request, CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId);
        if (chatSession == null)
        {
            return Result.Failure<bool>(Error.NotFound("ChatSession.NotFound", "Chat session not found"));
        }

        chatSession.UpdateTitle(request.Title);

        if (request.FolderId.HasValue)
        {
            chatSession.MoveToFolder(request.FolderId);
        }
        else
        {
            chatSession.RemoveFromFolder();
        }
        
        if (request.EnableThinking.HasValue)
        {
            chatSession.ToggleThinking(request.EnableThinking.Value);
        }

        await _chatSessionRepository.UpdateAsync(chatSession, cancellationToken);
        return Result.Success(true);
    }
} 