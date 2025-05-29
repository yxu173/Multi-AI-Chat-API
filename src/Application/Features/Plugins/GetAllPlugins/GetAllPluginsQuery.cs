using Application.Abstractions.Messaging;

namespace Application.Features.Plugins.GetAllPlugins;

public sealed record GetAllPluginsQuery() : IQuery<IReadOnlyList<PluginResponse>>;