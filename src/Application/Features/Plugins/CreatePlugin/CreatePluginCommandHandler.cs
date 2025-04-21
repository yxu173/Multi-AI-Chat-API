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

    public async Task<Result<Guid>> Handle(CreatePluginCommand request, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Create(request.Name, request.Description, request.IconUrl);
        await _pluginRepository.AddAsync(plugin);
        return Result.Success(plugin.Id);
    }
}