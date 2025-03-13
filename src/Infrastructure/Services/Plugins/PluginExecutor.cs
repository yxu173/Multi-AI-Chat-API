using Application.Abstractions.Interfaces;

namespace Infrastructure.Services.Plugins;

public class PluginExecutor : IPluginExecutor
{
    private readonly IChatPlugin _plugin;
    public Guid PluginId { get; }

    public PluginExecutor(IChatPlugin plugin, Guid pluginId)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        PluginId = pluginId;
    }

    public bool CanHandle(string userMessage)
    {
        return _plugin.CanHandle(userMessage);
    }

    public Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        return _plugin.ExecuteAsync(userMessage, cancellationToken);
    }
}