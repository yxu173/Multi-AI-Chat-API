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
        
        RegisterPlugin(new Guid("0d2987a6-e3ac-4de8-bd4e-95ede2892e9b"), typeof(WebSearchPlugin), "google_search");
        RegisterPlugin(new Guid("6A2ADF8A-9B2F-4F9D-8F31-B29A9F1C1760"), typeof(PerplexityPlugin), "Perplexity AI");
        RegisterPlugin(new Guid("f2726b0b-3759-4088-9bbe-4d094adc9f06"), typeof(JinaWebPlugin), "jina_web_reader");
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