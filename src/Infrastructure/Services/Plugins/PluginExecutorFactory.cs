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
        
        RegisterPlugin(new Guid("74a08c15-7cc7-436f-8734-25d65a9702d6"), typeof(WebSearchPlugin), "Web Search");
        RegisterPlugin(new Guid("6A2ADF8A-9B2F-4F9D-8F31-B29A9F1C1760"), typeof(PerplexityPlugin), "Perplexity AI");
        RegisterPlugin(new Guid("B4BDAF1C-7E6A-4B7D-8F31-B29A9F1C1760"), typeof(JinaWebPlugin), "Jina Web");
        RegisterPlugin(new Guid("B4BDAF1C-7E6A-4B7D-8F31-B29A9F1C1760"), typeof(WebPageReader), "Web Page Reader");
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