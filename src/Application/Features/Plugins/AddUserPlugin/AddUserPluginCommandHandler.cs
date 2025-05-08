using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Plugins.AddUserPlugin;

public sealed class AddUserPluginCommandHandler : ICommandHandler<AddUserPluginCommand, Guid>
{
    private readonly IUserPluginRepository _userPluginRepository;

    public AddUserPluginCommandHandler(IUserPluginRepository userPluginRepository)
    {
        _userPluginRepository = userPluginRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(AddUserPluginCommand command, CancellationToken ct)
    {
        var userPlugin = UserPlugin.Create(command.UserId, command.PluginId);
        await _userPluginRepository.AddAsync(userPlugin);
        return Result.Success(userPlugin.Id);
    } 
}