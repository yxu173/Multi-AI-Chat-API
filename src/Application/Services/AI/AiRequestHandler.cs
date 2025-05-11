using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions.Interfaces;
using Application.Services.AI.Builders;
using Application.Services.AI.Interfaces;
using Application.Services.AI.PayloadBuilders;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI;

public record FunctionDefinitionDto(
    string Name,
    string? Description,
    object? Parameters
);

public record AiRequestContext(
    Guid UserId,
    ChatSession ChatSession,
    List<MessageDto> History,
    AiAgent? AiAgent,
    UserAiModelSettings? UserSettings,
    AiModel SpecificModel,
    bool? RequestSpecificThinking = null,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool? EnableSafetyChecker = null,
    string? SafetyTolerance = null,
    List<FunctionDefinitionDto>? Functions = null,
    string? FunctionCall = null
);

public class AiRequestHandler : IAiRequestHandler
{
    private static readonly Regex _imageTagRegex = new(@"<image:([0-9a-fA-F-]{36})>", RegexOptions.Compiled);
    private static readonly Regex _fileTagRegex = new(@"<file:([0-9a-fA-F-]{36}):([^>]*)>", RegexOptions.Compiled);

    private readonly ILogger<AiRequestHandler> _logger;
    private readonly IPayloadBuilderFactory _payloadBuilderFactory;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly string _apiBaseUrl;

    public AiRequestHandler(
        IPayloadBuilderFactory payloadBuilderFactory,
        IPluginExecutorFactory pluginExecutorFactory,
        IChatSessionPluginRepository chatSessionPluginRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        ILogger<AiRequestHandler> logger)
    {
        _payloadBuilderFactory = payloadBuilderFactory ?? throw new ArgumentNullException(nameof(payloadBuilderFactory));
        _pluginExecutorFactory = pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _chatSessionPluginRepository = chatSessionPluginRepository ?? throw new ArgumentNullException(nameof(chatSessionPluginRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiRequestPayload> PrepareRequestPayloadAsync(AiRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ChatSession);
        ArgumentNullException.ThrowIfNull(context.SpecificModel);
        ArgumentNullException.ThrowIfNull(context.History);

        var processedHistory = new List<MessageDto>();
        foreach (var message in context.History)
        {
            var processedContent = await ProcessFileTagsAsync(message.Content, cancellationToken);
            
            var processedMessage = new MessageDto(
                processedContent, 
                message.IsFromAi, 
                message.MessageId)
            {
                FileAttachments = message.FileAttachments,
                ThinkingContent = message.ThinkingContent,
                FunctionCall = message.FunctionCall,
                FunctionResponse = message.FunctionResponse
            };
            
            processedHistory.Add(processedMessage);
        }
        
        var updatedContext = context with { History = processedHistory };

        var modelType = updatedContext.SpecificModel.ModelType;
        var chatId = updatedContext.ChatSession.Id;
        
        bool modelMightSupportTools = modelType is ModelType.OpenAi or ModelType.Anthropic or ModelType.Gemini or ModelType.DeepSeek or ModelType.Grok or ModelType.Qwen;
        List<object>? toolDefinitions = null;

        if (modelMightSupportTools)
        {
            var activePlugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatId, cancellationToken);
            var activePluginIds = activePlugins.Select(p => p.PluginId).ToList();

            if (activePluginIds.Any())
            {
                _logger?.LogInformation("Found {Count} active plugins for ChatSession {ChatId}", activePluginIds.Count, chatId);
                toolDefinitions = GetToolDefinitionsForPayload(modelType, activePluginIds);
                
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
                        
                        updatedContext = updatedContext with { Functions = functions, FunctionCall = "auto" };
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error converting tool definitions to function definitions for {ModelType}", modelType);
                    }
                }
            }
            else
            {
                _logger?.LogInformation("No active plugins found for ChatSession {ChatId}", chatId);
            }
        }
        else
        {
             _logger?.LogDebug("Tool support check skipped for model type {ModelType}", modelType);
        }

        try
        {
            AiRequestPayload payload = modelType switch
            {
                ModelType.OpenAi => await _payloadBuilderFactory.CreateOpenAiBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.Anthropic => await _payloadBuilderFactory.CreateAnthropicBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.Gemini => await _payloadBuilderFactory.CreateGeminiBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.DeepSeek => await _payloadBuilderFactory.CreateDeepSeekBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.AimlFlux => await _payloadBuilderFactory.CreateAimlFluxBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.Imagen => await _payloadBuilderFactory.CreateImagenBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.Grok => await _payloadBuilderFactory.CreateGrokBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                ModelType.Qwen => await _payloadBuilderFactory.CreateQwenBuilder().PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken),
                _ => throw new NotSupportedException($"Model type {modelType} is not supported for request preparation."),
            };
            return payload;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error preparing payload for {ModelType}", modelType);
            throw;
        }
    }

    private List<object>? GetToolDefinitionsForPayload(ModelType modelType, List<Guid> activePluginIds)
    {
        if (activePluginIds == null || !activePluginIds.Any()) return null;

        var allDefinitions = _pluginExecutorFactory.GetAllPluginDefinitions().ToList();
        if (!allDefinitions.Any())
        {
            _logger?.LogDebug("No plugin definitions found in the factory.");
            return null;
        }

        var activeDefinitions = allDefinitions
            .Where(def => activePluginIds.Contains(def.Id))
            .ToList();

        if (!activeDefinitions.Any())
        {
            _logger?.LogWarning("No matching definitions found in factory for active plugin IDs: {ActiveIds}", string.Join(", ", activePluginIds));
            return null;
        }

        _logger?.LogInformation("Found {DefinitionCount} active plugin definitions to format for {ModelType}.", activeDefinitions.Count, modelType);
        var formattedDefinitions = new List<object>();

        foreach (var def in activeDefinitions)
        {
            if (def.ParametersSchema == null)
            {
                _logger?.LogWarning("Skipping tool definition for {ToolName} ({ToolId}) due to missing parameter schema.", def.Name, def.Id);
                continue;
            }

            try
            {
                switch (modelType)
                {
                    case ModelType.OpenAi:
                        formattedDefinitions.Add(new
                        {
                            type = "function",
                            name = def.Name,
                            description = def.Description,
                            parameters = def.ParametersSchema
                        });
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
                        _logger?.LogWarning("Tool definition formatting for DeepSeek is not yet defined/supported. Skipping tool: {ToolName}", def.Name);
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
                        _logger?.LogWarning("Tool definition requested for provider {ModelType} which may not support the standard format or is unknown. Skipping tool: {ToolName}", modelType, def.Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error formatting tool definition for {ToolName} ({ToolId}) for provider {ModelType}", def.Name, def.Id, modelType);
            }
        }

        if (!formattedDefinitions.Any())
        {
            _logger?.LogWarning("No tool definitions could be formatted successfully for {ModelType}.", modelType);
            return null;
        }

        _logger?.LogInformation("Successfully formatted {FormattedCount} tool definitions for {ModelType}.", formattedDefinitions.Count, modelType);
        return formattedDefinitions;
    }

    private async Task<string> ProcessFileTagsAsync(string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        try
        {
            var imageMatches = _imageTagRegex.Matches(content);
            foreach (Match match in imageMatches)
            {
                if (Guid.TryParse(match.Groups[1].Value, out Guid fileId))
                {
                    var base64Data = await GetFileBase64Async(fileId, cancellationToken);
                    if (base64Data != null && !string.IsNullOrEmpty(base64Data.Base64Content))
                    {
                        var replacement = $"<image-base64:{base64Data.ContentType};base64,{base64Data.Base64Content}>";
                        content = content.Replace(match.Value, replacement);
                    }
                    else
                    {
                        content = content.Replace(match.Value, "[Image could not be processed]");
                    }
                }
            }

            var fileMatches = _fileTagRegex.Matches(content);
            foreach (Match match in fileMatches)
            {
                if (Guid.TryParse(match.Groups[1].Value, out Guid fileId))
                {
                    string fileName = match.Groups[2].Value;
                    var base64Data = await GetFileBase64Async(fileId, cancellationToken);
                    if (base64Data != null && !string.IsNullOrEmpty(base64Data.Base64Content))
                    {
                        var replacement = $"<file-base64:{fileName}:{base64Data.ContentType};base64,{base64Data.Base64Content}>";
                        content = content.Replace(match.Value, replacement);
                    }
                    else
                    {
                        content = content.Replace(match.Value, $"[File {fileName} could not be processed]");
                    }
                }
            }

            return content;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing file tags in content");
            return content;
        }
    }

    private async Task<FileBase64Data?> GetFileBase64Async(Guid fileId, CancellationToken cancellationToken)
    {
        try
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
            if (fileAttachment == null) return null;
            if (!File.Exists(fileAttachment.FilePath)) return null;
            var bytes = await File.ReadAllBytesAsync(fileAttachment.FilePath, cancellationToken);
            var base64 = Convert.ToBase64String(bytes);
            return new FileBase64Data
            {
                Base64Content = base64,
                ContentType = fileAttachment.ContentType,
                FileName = fileAttachment.FileName,
                FileType = fileAttachment.FileType.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting base64 for file {FileId}", fileId);
            return null;
        }
    }

    private class FileBase64Data
    {
        public string Base64Content { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }
}