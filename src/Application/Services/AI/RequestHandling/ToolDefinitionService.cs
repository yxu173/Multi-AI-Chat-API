using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI.Interfaces;
using Application.Services.AI.RequestHandling.Interfaces;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.RequestHandling;

public class ToolDefinitionService : IToolDefinitionService
{
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly IUserPluginRepository _userPluginRepository;
    private readonly ILogger<ToolDefinitionService> _logger;

    public ToolDefinitionService(
        IPluginExecutorFactory pluginExecutorFactory,
        IUserPluginRepository userPluginRepository,
        ILogger<ToolDefinitionService> logger)
    {
        _pluginExecutorFactory = pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _userPluginRepository = userPluginRepository ?? throw new ArgumentNullException(nameof(userPluginRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Define a list of plugins that require explicit user activation
    private static readonly HashSet<Guid> _requiresExplicitActivation = new HashSet<Guid>
    {
        // Jina DeepSearch plugin requires user activation
        new Guid("3d5ec31c-5e6c-437d-8494-2ca942c9e2fe")
    };

    // Define provider-specific plugins
    private static readonly Dictionary<ModelType, HashSet<Guid>> _providerSpecificPlugins = new()
    {
        // DeepWiki MCP and Code Interpreter plugins are only for OpenAI
        [ModelType.OpenAi] = new HashSet<Guid>
        {
            new Guid("b7e7e7e7-e7e7-4e7e-8e7e-e7e7e7e7e7e7"), // DeepWiki MCP
            new Guid("c0de1e7e-7e7e-4e7e-8e7e-e7e7e7e7c0de")  // Code Interpreter
        }
    };

    public async Task<List<PluginDefinition>?> GetToolDefinitionsAsync(
        Guid userId,
        bool enableDeepSearch,
        ModelType? modelType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all user plugin preferences
            var userPlugins = (await _userPluginRepository.GetAllByUserIdAsync(userId)).ToList();

            // Get plugins that are explicitly disabled
            var disabledPluginIds = userPlugins
                .Where(up => !up.IsEnabled)
                .Select(up => up.PluginId)
                .ToHashSet();

            // Get plugins that are explicitly enabled (needed for plugins requiring activation)
            var explicitlyEnabledPluginIds = userPlugins
                .Where(up => up.IsEnabled)
                .Select(up => up.PluginId)
                .ToHashSet();

            var allPluginDefinitions = _pluginExecutorFactory.GetAllPluginDefinitions().ToList();
            var allPluginIds = allPluginDefinitions.Select(d => d.Id);

            // For normal plugins: active unless disabled
            // For plugins requiring activation: only active if explicitly enabled
            var activePluginIds = allPluginIds
                .Where(id => !disabledPluginIds.Contains(id) &&
                             (!_requiresExplicitActivation.Contains(id) || explicitlyEnabledPluginIds.Contains(id)))
                .ToHashSet();

            // Filter out provider-specific plugins that don't match the current model type
            if (modelType.HasValue)
            {
                var pluginsToExclude = new HashSet<Guid>();
                foreach (var kvp in _providerSpecificPlugins)
                {
                    if (kvp.Key != modelType.Value)
                    {
                        foreach (var pluginId in kvp.Value)
                        {
                            pluginsToExclude.Add(pluginId);
                        }
                    }
                }
                
                activePluginIds.ExceptWith(pluginsToExclude);
                
                _logger.LogInformation(
                    "Filtered plugins for model type {ModelType}: Excluded {ExcludedCount} provider-specific plugins",
                    modelType.Value, pluginsToExclude.Count);
            }

            _logger.LogInformation(
                "Plugin activation: Standard plugins: {StandardCount}, Activation-required plugins: {ActivationCount}, User-enabled: {EnabledCount}, User-disabled: {DisabledCount}",
                allPluginIds.Count(id => !_requiresExplicitActivation.Contains(id)),
                _requiresExplicitActivation.Count,
                explicitlyEnabledPluginIds.Count,
                disabledPluginIds.Count
            );

            if (!activePluginIds.Any())
            {
                _logger.LogInformation("No active plugins found for User {UserId}", userId);
                return null;
            }

            _logger.LogInformation("Found {Count} active plugins after user preferences for User {UserId}", activePluginIds.Count, userId);

            var activeDefinitions = allPluginDefinitions
                .Where(def => activePluginIds.Contains(def.Id))
                .ToList();

            if (enableDeepSearch)
            {
                var deepSearchPluginId = new Guid("3d5ec31c-5e6c-437d-8494-2ca942c9e2fe");
                if (activeDefinitions.All(d => d.Id != deepSearchPluginId))
                {
                    var deepSearchDefinition = allPluginDefinitions.FirstOrDefault(d => d.Id == deepSearchPluginId);
                    if (deepSearchDefinition != null)
                    {
                        activeDefinitions.Add(deepSearchDefinition);
                        _logger.LogInformation("Force-enabled Deep Search plugin for this request.");
                    }
                }
            }
            
            if (!activeDefinitions.Any())
            {
                _logger.LogWarning("No matching definitions found in factory for active plugin IDs: {ActiveIds}", string.Join(", ", activePluginIds));
                return null;
            }
            
            _logger.LogInformation("Successfully found {DefinitionCount} active plugin definitions for User {UserId}", activeDefinitions.Count, userId);

            return activeDefinitions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool definitions for user {UserId}", userId);
            return null;
        }
    }
}
