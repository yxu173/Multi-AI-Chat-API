using Application.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.Plugins;

public class PluginExecutorFactory : IPluginExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictionary<Guid, Type> _pluginRegistry;

    public PluginExecutorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _pluginRegistry = new Dictionary<Guid, Type>
        {
            { Guid.NewGuid(), typeof(PerplexityPlugin) },
            { Guid.NewGuid(), typeof(WebSearchPlugin) }
        };
    }

    public IPluginExecutor GetExecutor(Guid pluginId)
    {
        if (!_pluginRegistry.TryGetValue(pluginId, out var pluginType))
        {
            throw new ArgumentException($"No plugin registered with ID: {pluginId}");
        }

        var plugin = (IChatPlugin)_serviceProvider.GetRequiredService(pluginType);
        return new PluginExecutor(plugin, pluginId);
    }
}