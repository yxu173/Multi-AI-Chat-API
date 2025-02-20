using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.CreateChatSession;

public class CreateChatSessionCommandHandler : ICommandHandler<CreateChatSessionCommand, Guid>
{
    private readonly IChatRepository _chatRepository;

    public CreateChatSessionCommandHandler(IChatRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }
    public async Task<Result<Guid>> Handle(CreateChatSessionCommand request, CancellationToken cancellationToken)
    {
        var modelType = Enum.Parse<ModelType>(request.ModelType);
        var chatSession = ChatSession.Create(request.UserId,modelType);
        await _chatRepository.CreateChatSessionAsync(chatSession);
        return Result.Success(chatSession.Id);
    }
}