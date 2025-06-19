using System.Text.Json.Nodes;

namespace Application.Abstractions.Interfaces;

// Option 1: Keep it simple, GetPlugin stays, another service handles definitions
// public interface IPluginExecutorFactory
// {
//     IChatPlugin GetPlugin(Guid pluginId);
// }

// Option 2: Make the factory responsible for providing definitions too
public interface IPluginExecutorFactory
{
    /// <summary>
    /// Gets a specific plugin implementation by its ID.
    /// </summary>
    IChatPlugin<string> GetPlugin(Guid pluginId);

    /// <summary>
    /// Gets definitions for all registered plugins, suitable for AI tool descriptions.
    /// </summary>
    /// <returns>A collection of plugin definitions.</returns>
    IEnumerable<PluginDefinition> GetAllPluginDefinitions(); // New method
}

/// <summary>
/// Represents the definition of a plugin for AI tool use.
/// </summary>
public record PluginDefinition(
    Guid Id,
    string Name,
    string Description,
    JsonObject? ParametersSchema // Schema describing expected arguments
);