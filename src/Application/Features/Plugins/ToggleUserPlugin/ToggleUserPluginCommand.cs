using Application.Abstractions.Messaging;

namespace Application.Features.Plugins.ToggleUserPlugin;

public sealed record ToggleUserPluginCommand(Guid UserId, Guid PluginId, bool IsEnabled) : ICommand<Guid>;
