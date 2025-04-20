using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.DeleteChatById;

public sealed class DeleteChatCommandHandler : ICommandHandler<DeleteChatCommand, bool>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public DeleteChatCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> Handle(DeleteChatCommand request, CancellationToken cancellationToken)
    {
        var result = await _chatSessionRepository.DeleteAsync(request.Id, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.Error);
        }
        return Result.Success(true);
    }
}