using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.CreateChatSession;

public class CreateChatSessionCommandHandler : ICommandHandler<CreateChatSessionCommand, Guid>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public CreateChatSessionCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<Guid>> Handle(CreateChatSessionCommand request, CancellationToken cancellationToken)
    {
        var chatSession = ChatSession.Create(request.UserId, request.ModelId, request.FolderId, request.customApiKey);
        await _chatSessionRepository.AddAsync(chatSession);
        return Result.Success(chatSession.Id);
    }
}