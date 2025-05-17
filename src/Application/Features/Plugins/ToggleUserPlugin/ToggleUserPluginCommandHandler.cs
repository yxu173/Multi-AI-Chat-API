using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Plugins.ToggleUserPlugin;

public sealed class ToggleUserPluginCommandHandler : ICommandHandler<ToggleUserPluginCommand, Guid>
{
    private readonly IUserPluginRepository _userPluginRepository;
    private readonly IPluginRepository _pluginRepository;

    public ToggleUserPluginCommandHandler(
        IUserPluginRepository userPluginRepository,
        IPluginRepository pluginRepository)
    {
        _userPluginRepository = userPluginRepository;
        _pluginRepository = pluginRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(ToggleUserPluginCommand command, CancellationToken ct)
    {
        var plugin = await _pluginRepository.GetByIdAsync(command.PluginId);
        if (plugin == null)
        {
            return Result.Failure<Guid>(Error.NotFound($"Plugin with ID {command.PluginId} not found.",
                $"Plugin with ID {command.PluginId} not found."));
        }

        var userPlugin = await _userPluginRepository.GetByUserIdAndPluginIdAsync(command.UserId, command.PluginId);

        if (userPlugin == null)
        {
            userPlugin = UserPlugin.Create(command.UserId, command.PluginId, command.IsEnabled);
            await _userPluginRepository.AddAsync(userPlugin);
        }
        else
        {
            userPlugin.SetEnabled(command.IsEnabled);
            await _userPluginRepository.UpdateAsync(userPlugin);
        }

        return Result.Success(userPlugin.Id);
    }
}