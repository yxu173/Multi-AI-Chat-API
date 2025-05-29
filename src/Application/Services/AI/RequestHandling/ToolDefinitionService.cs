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

    public async Task<List<object>?> GetToolDefinitionsAsync(
        Guid userId, 
        ModelType modelType, 
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
                .ToList();
                
            _logger.LogInformation(
                "Plugin activation: Standard plugins: {StandardCount}, Activation-required plugins: {ActivationCount}, User-enabled: {EnabledCount}, User-disabled: {DisabledCount}", 
                allPluginIds.Count(id => !_requiresExplicitActivation.Contains(id)),
                _requiresExplicitActivation.Count,
                explicitlyEnabledPluginIds.Count,
                disabledPluginIds.Count
            );

            List<object>? toolDefinitions = null;
            
            if (activePluginIds.Any())
            {
                _logger.LogInformation("Found {Count} active plugins after user preferences for User {UserId}", activePluginIds.Count, userId);
                toolDefinitions = GetToolDefinitionsForPayload(modelType, activePluginIds);
                
                // Special handling for Grok and Qwen which need function definitions in a specific format
                if ((modelType == ModelType.Grok || modelType == ModelType.Qwen) && toolDefinitions != null && toolDefinitions.Any())
                {
                    try
                    {
                        var functions = new List<FunctionDefinitionDto>();
                        
                        foreach (var tool in toolDefinitions)
                        {
                            string json = JsonSerializer.Serialize(tool);
                            using JsonDocument doc = JsonDocument.Parse(json);
                            
                            string? name = null;
                            string? description = null;
                            object? parameters = null;
                            
                            if (doc.RootElement.TryGetProperty("function", out var functionElement))
                            {
                                if (functionElement.TryGetProperty("name", out var nameElement))
                                {
                                    name = nameElement.GetString();
                                }
                                
                                if (functionElement.TryGetProperty("description", out var descElement))
                                {
                                    description = descElement.GetString();
                                }
                                
                                if (functionElement.TryGetProperty("parameters", out var paramsElement))
                                {
                                    parameters = JsonSerializer.Deserialize<object>(paramsElement.GetRawText());
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(name))
                            {
                                functions.Add(new FunctionDefinitionDto(name, description, parameters));
                            }
                        }
                        
                        // Note: we don't update the context here - that's handled at the AiRequestHandler level
                        _logger.LogDebug("Processed {FunctionCount} functions for {ModelType}", functions.Count, modelType);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error converting tool definitions to function definitions for {ModelType}", modelType);
                    }
                }
            }
            else
            {
                _logger.LogInformation("No active plugins found for User {UserId}", userId);
            }
            
            return toolDefinitions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool definitions for user {UserId}", userId);
            return null;
        }
    }

    private List<object>? GetToolDefinitionsForPayload(ModelType modelType, List<Guid> activePluginIds)
    {
        try 
        {
            if (activePluginIds == null || !activePluginIds.Any())
            {
                _logger.LogDebug("No active plugin IDs provided");
                return null;
            }

            _logger.LogInformation("Getting tool definitions for {Count} active plugin IDs", activePluginIds.Count);
            
            var allDefinitions = _pluginExecutorFactory.GetAllPluginDefinitions().ToList();
            _logger.LogDebug("Found {Count} total plugin definitions in factory", allDefinitions.Count);
            
            if (!allDefinitions.Any())
            {
                _logger.LogDebug("No plugin definitions found in the factory.");
                return null;
            }

            var activeDefinitions = allDefinitions
                .Where(def => activePluginIds.Contains(def.Id))
                .ToList();

            if (!activeDefinitions.Any())
            {
                _logger.LogWarning("No matching definitions found in factory for active plugin IDs: {ActiveIds}", string.Join(", ", activePluginIds));
                return null;
            }

            _logger.LogInformation("Found {DefinitionCount} active plugin definitions to format for {ModelType}.", activeDefinitions.Count, modelType);
            var formattedDefinitions = new List<object>();

            foreach (var def in activeDefinitions)
            {
                if (def.ParametersSchema == null)
                {
                    _logger.LogWarning("Skipping tool definition for {ToolName} ({ToolId}) due to missing parameter schema.", def.Name, def.Id);
                    continue;
                }

                try
                {
                    switch (modelType)
                    {
                        case ModelType.OpenAi:
                            // OpenAI requires the format with type, name, description, and parameters directly
                            var functionObj = new
                            {
                                type = "function",
                                name = def.Name,
                                description = def.Description,
                                parameters = def.ParametersSchema
                            };
                            
                            // Log the function object to debug any issues
                            _logger.LogDebug("OpenAI function format: {Function}", System.Text.Json.JsonSerializer.Serialize(functionObj));
                            
                            formattedDefinitions.Add(functionObj);
                            break;

                        case ModelType.Anthropic:
                            formattedDefinitions.Add(new
                            {
                                name = def.Name,
                                description = def.Description,
                                input_schema = def.ParametersSchema
                            });
                            break;

                        case ModelType.Gemini:
                            formattedDefinitions.Add(new
                            {
                                name = def.Name,
                                description = def.Description,
                                parameters = def.ParametersSchema
                            });
                            break;
                            
                        case ModelType.DeepSeek:
                            _logger.LogWarning("Tool definition formatting for DeepSeek is not yet defined/supported. Skipping tool: {ToolName}", def.Name);
                            break;

                        case ModelType.Grok:
                            formattedDefinitions.Add(new 
                            {
                                type = "function",
                                function = new
                                {
                                    name = def.Name,
                                    description = def.Description,
                                    parameters = def.ParametersSchema
                                }
                            });
                            break;

                        case ModelType.Qwen:
                            formattedDefinitions.Add(new 
                            {
                                type = "function",
                                function = new
                                {
                                    name = def.Name,
                                    description = def.Description,
                                    parameters = def.ParametersSchema
                                }
                            });
                            break;
                            
                        default:
                            _logger.LogWarning("Tool definition requested for provider {ModelType} which may not support the standard format or is unknown. Skipping tool: {ToolName}", modelType, def.Name);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error formatting tool definition for {ToolName} ({ToolId}) for provider {ModelType}", def.Name, def.Id, modelType);
                }
            }

            if (!formattedDefinitions.Any())
            {
                _logger.LogWarning("No tool definitions could be formatted successfully for {ModelType}.", modelType);
                return null;
            }

            _logger.LogInformation("Successfully formatted {FormattedCount} tool definitions for {ModelType}.", formattedDefinitions.Count, modelType);
            return formattedDefinitions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while getting tool definitions for payload: {Error}", ex.Message);
            return null;
        }
    }
    
    public record FunctionDefinitionDto(
        string Name,
        string? Description,
        object? Parameters
    );
}
