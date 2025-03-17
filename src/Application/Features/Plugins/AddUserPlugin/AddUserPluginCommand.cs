using Application.Abstractions.Messaging;

namespace Application.Features.Plugins.AddUserPlugin;

public sealed record AddUserPluginCommand(Guid UserId, Guid PluginId) : ICommand<Guid>;