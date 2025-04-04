using Application.Abstractions.Interfaces;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Application.Services;

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

    /// <summary>
    /// Executes a specific plugin by its ID with the arguments provided (typically by the AI).
    /// </summary>
    /// <param name="pluginId">The GUID of the plugin to execute.</param>
    /// <param name="arguments">The arguments for the plugin execution, as a JsonObject.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the plugin execution.</returns>
    public async Task<PluginResult> ExecutePluginByIdAsync(Guid pluginId, JsonObject? arguments, CancellationToken cancellationToken = default)
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
            return new PluginResult("", false, $"Plugin with ID {pluginId} not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error executing plugin with ID: {PluginId}", pluginId);
            return new PluginResult("", false, $"An unexpected error occurred while executing plugin {pluginId}: {ex.Message}");
        }
    }
}