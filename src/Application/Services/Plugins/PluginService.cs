using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Services.Plugins;

public class PluginService
{
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly ILogger<PluginService>? _logger;

    public PluginService(
        IPluginExecutorFactory pluginExecutorFactory,
        ILogger<PluginService>? logger = null)
    {
        _pluginExecutorFactory = pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _logger = logger;
    }
    
    public async Task<PluginResult<string>> ExecutePluginByIdAsync(Guid pluginId, JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("Attempting to execute plugin with ID: {PluginId}", pluginId);

            var plugin = _pluginExecutorFactory.GetPlugin(pluginId);

            var result = await plugin.ExecuteAsync(arguments, cancellationToken);

            if (!result.Success)
            {
                _logger?.LogWarning("Plugin execution failed for {PluginName} ({PluginId}). Error: {Error}", plugin.Name, pluginId, result.ErrorMessage);
            }
            else
            {
                _logger?.LogInformation("Plugin {PluginName} ({PluginId}) executed successfully.", plugin.Name, pluginId);
            }

            return result;
        }
        catch (ArgumentException argEx)
        {
            _logger?.LogError(argEx, "Failed to find plugin with ID: {PluginId}", pluginId);
            return new PluginResult<string>("", false, $"Plugin with ID {pluginId} not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error executing plugin with ID: {PluginId}", pluginId);
            return new PluginResult<string>("", false, $"An unexpected error occurred while executing plugin {pluginId}: {ex.Message}");
        }
    }
}