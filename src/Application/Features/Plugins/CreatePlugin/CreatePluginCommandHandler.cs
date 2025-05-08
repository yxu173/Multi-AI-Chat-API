using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Plugins.CreatePlugin;

public sealed class CreatePluginCommandHandler : ICommandHandler<CreatePluginCommand, Guid>
{
    private readonly IPluginRepository _pluginRepository;

    public CreatePluginCommandHandler(IPluginRepository pluginRepository)
    {
        _pluginRepository = pluginRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(CreatePluginCommand command, CancellationToken ct)
    {
        var plugin = Plugin.Create(command.Name, command.Description, command.IconUrl);
        await _pluginRepository.AddAsync(plugin);
        return Result.Success(plugin.Id);
    }

}