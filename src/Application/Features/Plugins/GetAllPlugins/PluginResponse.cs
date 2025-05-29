namespace Application.Features.Plugins.GetAllPlugins;

public sealed record PluginResponse(
    Guid Id,
    string Name,
    string Description,
    string IconUrl
);