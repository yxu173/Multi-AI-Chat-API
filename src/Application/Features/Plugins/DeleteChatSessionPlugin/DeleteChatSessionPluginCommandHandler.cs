using Domain.Repositories;
using FastEndpoints;
using SharedKernel;

namespace Application.Features.Plugins.DeleteChatSessionPlugin;

public sealed class DeleteChatSessionPluginCommandHandler : ICommandHandler<DeleteChatSessionPluginCommand, Result<bool>>
{
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository;

    public DeleteChatSessionPluginCommandHandler(IChatSessionPluginRepository chatSessionPluginRepository)
    {
        _chatSessionPluginRepository = chatSessionPluginRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(DeleteChatSessionPluginCommand command, CancellationToken ct)
    {
        await _chatSessionPluginRepository.DeleteAsync(command.Id, ct);
        
        return Result.Success(true);
    }
}