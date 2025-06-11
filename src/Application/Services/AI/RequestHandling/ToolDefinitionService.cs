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
        new Guid("b8d00934-726c-4cc2-8198-ee25ab2f3154")
    };

    public async Task<List<PluginDefinition>?> GetToolDefinitionsAsync(
        Guid userId,
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
