using Application.Abstractions.Messaging;

namespace Application.Features.Plugins.AddPluginToChat;

public sealed record AddPluginToChatCommand(Guid ChatId, Guid PluginId, int Order) : ICommand<Guid>;