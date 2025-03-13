namespace Web.Api.Contracts.Plugins;

public sealed record AddChatPluginRequest(
    Guid ChatId,
    Guid PluginId,
    int Order);