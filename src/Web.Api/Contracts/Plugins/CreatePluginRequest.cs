namespace Web.Api.Contracts.Plugins;

public sealed record CreatePluginRequest(
    string Name,
    string Description,
    string IconUrl
);