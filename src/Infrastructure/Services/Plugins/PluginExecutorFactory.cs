using Application.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.Plugins;

public class PluginExecutorFactory : IPluginExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictionary<Guid, PluginInfoDto> _pluginRegistry;

    public PluginExecutorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _pluginRegistry = new Dictionary<Guid, PluginInfoDto>();

        // Register plugins with fixed GUIDs
        RegisterPlugin(new Guid("8F0F9A5D-9C9F-4D1A-A033-C913E5FBE428"), typeof(WebSearchPlugin), "Web Search");
        RegisterPlugin(new Guid("6A2ADF8A-9B2F-4F9D-8F31-B29A9F1C1760"), typeof(PerplexityPlugin), "Perplexity AI");
    }

    private void RegisterPlugin(Guid id, Type pluginType, string name)
    {
        _pluginRegistry[id] = new PluginInfoDto { Id = id, Type = pluginType, Name = name };
    }

    public IChatPlugin GetPlugin(Guid pluginId)
    {
        if (!_pluginRegistry.TryGetValue(pluginId, out var pluginInfo))
        {
            throw new ArgumentException($"No plugin registered with ID: {pluginId}");
        }
        return (IChatPlugin)_serviceProvider.GetRequiredService(pluginInfo.Type);
    }

    public class PluginInfoDto
    {
        public Guid Id { get; set; }
        public Type Type { get; set; }
        public string Name { get; set; }
    }
}