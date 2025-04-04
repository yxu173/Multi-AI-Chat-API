using Application.Abstractions.Messaging;

namespace Application.Features.Plugins.CreatePlugin;

public sealed record CreatePluginCommand(string Name, string Description, string PluginType, string ParametersSchema) : ICommand<Guid>;