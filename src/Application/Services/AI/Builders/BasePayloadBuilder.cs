using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.PayloadBuilders;

public abstract class BasePayloadBuilder
{
    protected readonly ILogger Logger;

    protected BasePayloadBuilder(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected Dictionary<string, object> GetMergedParameters(AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        var model = context.SpecificModel;
        var agent = context.AiAgent;
        var userSettings = context.UserSettings;

        ModelParameters? sourceParams = null;
        if (agent?.AssignCustomModelParameters == true && agent.ModelParameter != null)
        {
            sourceParams = agent.ModelParameter;
            parameters["temperature"] = sourceParams.Temperature;
            parameters["top_p"] = sourceParams.TopP;
            parameters["top_k"] = sourceParams.TopK;
            parameters["frequency_penalty"] = sourceParams.FrequencyPenalty;
            parameters["presence_penalty"] = sourceParams.PresencePenalty;
            parameters["max_tokens"] = sourceParams.MaxTokens;
        }
        else if (userSettings != null)
        {
            parameters["temperature"] = userSettings.ModelParameters.Temperature;
            parameters["top_p"] = userSettings.ModelParameters.TopP;
            parameters["top_k"] = userSettings.ModelParameters.TopK;
            parameters["frequency_penalty"] = userSettings.ModelParameters.FrequencyPenalty;
            parameters["presence_penalty"] = userSettings.ModelParameters.PresencePenalty;
        }

        if (!parameters.ContainsKey("max_tokens") && model.MaxOutputTokens.HasValue)
        {
            parameters["max_tokens"] = model.MaxOutputTokens.Value;
        }

        return parameters;
    }

    protected void ApplyParametersToRequest(Dictionary<string, object> requestObj,
        Dictionary<string, object> parameters,
        ModelType modelType)
    {
        foreach (var kvp in parameters)
        {
            string standardParamName = kvp.Key;
            string providerParamName = GetProviderParameterName(standardParamName, modelType);

            if (IsParameterSupported(providerParamName, modelType))
            {
                object valueToSend = kvp.Value;
                requestObj[providerParamName] = valueToSend;
            }
            else
            {
                Logger?.LogDebug(
                    "Skipping unsupported parameter '{StandardName}' (mapped to '{ProviderName}') for model type {ModelType}",
                    standardParamName, providerParamName, modelType);
            }
        }
    }

    protected string GetProviderParameterName(string standardName, ModelType modelType)
    {
        if (modelType == ModelType.Gemini)
        {
            return standardName switch
            {
                "top_p" => "topP",
                "top_k" => "topK",
                "max_tokens" => "maxOutputTokens",
                "stop" => "stopSequences",
                _ => standardName
            };
        }

        if (modelType == ModelType.Anthropic)
        {
            return standardName switch
            {
                "stop" => "stop_sequences",
                "max_tokens" => "max_tokens",
                _ => standardName
            };
        }

        return standardName;
    }

    protected bool IsParameterSupported(string providerParamName, ModelType modelType)
    {
        switch (modelType)
        {
            case ModelType.OpenAi:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "temperature", "top_p", "max_tokens",
                    "seed", "response_format", "tools", "tool_choice", "logit_bias", "user", "n", "logprobs",
                    "top_logprobs", "reasoning"
                }.Contains(providerParamName);

            case ModelType.Anthropic:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "max_tokens", "temperature", "top_k", "top_p", "stop_sequences", "stream", "system", "messages",
                    "metadata", "model", "tools", "tool_choice", "thinking"
                }.Contains(providerParamName);

            case ModelType.Gemini:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "temperature", "topP", "topK", "maxOutputTokens", "stopSequences", "candidateCount",
                    "response_mime_type", "response_schema"
                }.Contains(providerParamName);

            case ModelType.DeepSeek:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "temperature", "top_p", "max_tokens", "stop", "frequency_penalty", "presence_penalty",
                    "logit_bias", "logprobs", "top_logprobs", "stream", "model", "messages", "n", "seed",
                    "response_format",
                    "enable_cot", "enable_reasoning", "reasoning_mode"
                }.Contains(providerParamName);

            case ModelType.AimlFlux: // Image generation parameters
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "prompt", "image_size", "num_images", "output_format", "enable_safety_checker", "safety_tolerance",
                    "seed"
                }.Contains(providerParamName);

            default:
                Logger?.LogWarning("Parameter support check requested for unknown ModelType: {ModelType}", modelType);
                return false;
        }
    }

    protected List<(string Role, string Content)> MergeConsecutiveRoles(List<(string Role, string Content)> messages)
    {
        if (messages == null || !messages.Any()) return new List<(string Role, string Content)>();

        var merged = new List<(string Role, string Content)>();
        var currentRole = messages[0].Role;
        var currentContent = new StringBuilder(messages[0].Content);

        for (int i = 1; i < messages.Count; i++)
        {
            if (messages[i].Role == currentRole)
            {
                if (currentContent.Length > 0 && !string.IsNullOrWhiteSpace(currentContent.ToString()))
                {
                    currentContent.AppendLine().AppendLine();
                }

                currentContent.Append(messages[i].Content);
            }
            else
            {
                merged.Add((currentRole, currentContent.ToString().Trim()));
                currentRole = messages[i].Role;
                currentContent.Clear().Append(messages[i].Content);
            }
        }

        merged.Add((currentRole, currentContent.ToString().Trim()));

        return merged.Where(m => !string.IsNullOrEmpty(m.Content)).ToList();
    }

    protected void EnsureAlternatingRoles(List<object> messages, string userRole, string modelRole)
    {
        if (messages == null || !messages.Any()) return;

        string firstRole = GetRoleFromDynamicMessage(messages[0]);
        if (firstRole != userRole)
        {
            Logger?.LogError(
                "History for {ModelRole} provider does not start with a {UserRole} message. First role: {FirstRole}. This might cause API errors.",
                modelRole, userRole, firstRole);
        }

        var cleanedMessages = new List<object>();
        if (messages.Count > 0)
        {
            cleanedMessages.Add(messages[0]);
            for (int i = 1; i < messages.Count; i++)
            {
                string previousRole = GetRoleFromDynamicMessage(cleanedMessages.Last());
                string currentRole = GetRoleFromDynamicMessage(messages[i]);

                if (currentRole != previousRole)
                {
                    cleanedMessages.Add(messages[i]);
                }
                else
                {
                    Logger?.LogWarning(
                        "Found consecutive \'{CurrentRole}\' roles at index {Index} for {ModelRole} provider.",
                        currentRole, i, modelRole);

                    bool handled = false;
                    if (modelRole == "assistant" && currentRole == userRole)
                    {
                        Logger?.LogWarning(
                            "Injecting placeholder '{ModelRole}' message to fix consecutive '{UserRole}' roles for Anthropic.",
                            modelRole, userRole);
                        cleanedMessages.Add(new { role = modelRole, content = "..." });
                        cleanedMessages.Add(messages[i]);
                        handled = true;
                    }
                    else if (modelRole == "model" && currentRole == modelRole)
                    {
                        Logger?.LogWarning("Merging consecutive '{ModelRole}' role content for Gemini.",
                            modelRole);
                        var lastMsg = cleanedMessages.Last();
                        var currentMsg = messages[i];
                        object mergedMessage = MergeGeminiMessages(lastMsg, currentMsg);
                        cleanedMessages.RemoveAt(cleanedMessages.Count - 1); // Remove previous model message
                        cleanedMessages.Add(mergedMessage);
                        handled = true;
                    }

                    if (!handled)
                    {
                        Logger?.LogError(
                            "Unhandled consecutive '{CurrentRole}' role at index {Index} for {ModelRole}. Skipping message to avoid potential API error. Original Message: {OriginalMessage}",
                            currentRole, i, modelRole, TrySerialize(messages[i]));
                    }
                }
            }
        }

        messages.Clear();
        messages.AddRange(cleanedMessages);
    }

    /// <summary>
    /// Merges two Gemini messages with the same role by concatenating their content parts.
    /// </summary>
    /// <param name="lastMessage">The previous message in the sequence</param>
    /// <param name="currentMessage">The current message to merge</param>
    /// <returns>A new merged message object</returns>
    protected object MergeGeminiMessages(dynamic lastMessage, dynamic currentMessage)
    {
        try
        {
            // Create a copy of the dictionary structure
            var result = new Dictionary<string, object>();
            if (lastMessage is IDictionary<string, object> lastDict)
            {
                foreach (var key in lastDict.Keys)
                {
                    result[key] = lastDict[key];
                }
            }
            
            // Handle the parts array which contains the content
            if (result.TryGetValue("parts", out var lastPartsObj) && lastPartsObj is List<object> lastParts &&
                currentMessage is IDictionary<string, object> currentDict &&
                currentDict.TryGetValue("parts", out var currentPartsObj) && currentPartsObj is List<object> currentParts)
            {
                // Combine the parts arrays
                var mergedParts = new List<object>(lastParts);
                mergedParts.AddRange(currentParts);
                result["parts"] = mergedParts;
                
                Logger?.LogInformation("Successfully merged {Count} parts from consecutive model messages", 
                    currentParts.Count);
                
                return result;
            }
            
            // Handle the content field for simpler message formats
            if (result.TryGetValue("content", out var lastContent) && lastContent is string lastContentStr &&
                currentMessage is IDictionary<string, object> currentMsgDict &&
                currentMsgDict.TryGetValue("content", out var currentContent) && currentContent is string currentContentStr)
            {
                // Concatenate the content strings
                result["content"] = lastContentStr + "\n" + currentContentStr;
                
                Logger?.LogInformation("Successfully merged content from consecutive model messages");
                
                return result;
            }
            
            
            return lastMessage;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error merging Gemini messages");
            return lastMessage;
        }
    }

    protected string GetRoleFromDynamicMessage(dynamic message)
    {
        try
        {
            if (message is IDictionary<string, object> dict && dict.TryGetValue("role", out var roleValue) &&
                roleValue is string roleStr)
            {
                return roleStr;
            }

            var roleProp = message.GetType().GetProperty("role");
            if (roleProp != null && roleProp.PropertyType == typeof(string))
            {
                var value = roleProp.GetValue(message);
                if (value is string roleVal)
                {
                    return roleVal;
                }
            }
        }
        catch (Exception ex)
        {
            //  Logger?.LogWarning(ex, "Could not determine role from dynamic message object of type {Type}. Message: {Message}", message?.GetType().Name ?? "null", TrySerialize(message));
        }


        return string.Empty;
    }

    protected string TrySerialize(object obj)
    {
        try
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            });
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}] - Type: {obj?.GetType().Name ?? "null"}";
        }
    }

    protected bool IsValidAnthropicImageType(string mimeType, out string normalizedMediaType)
    {
        normalizedMediaType = mimeType.ToLowerInvariant().Trim();
        var supported = new Dictionary<string, string>
        {
            { "image/jpeg", "image/jpeg" },
            { "image/png", "image/png" },
            { "image/gif", "image/gif" },
            { "image/webp", "image/webp" }
        };
        normalizedMediaType = supported.GetValueOrDefault(normalizedMediaType);
        return !string.IsNullOrEmpty(normalizedMediaType);
    }

    protected bool IsValidGeminiImageType(string mimeType, out string normalizedMediaType)
    {
        string lowerMime = mimeType?.ToLowerInvariant() ?? "";
        var supported = new Dictionary<string, string>
        {
            { "image/png", "image/png" },
            { "image/jpeg", "image/jpeg" },
            { "image/webp", "image/webp" },
            { "image/heic", "image/heic" },
            { "image/heif", "image/heif" }
        };
        normalizedMediaType = supported.GetValueOrDefault(lowerMime);
        return !string.IsNullOrEmpty(normalizedMediaType);
    }

    protected bool IsValidAnthropicDocumentType(string mimeType, out string normalizedMediaType)
    {
        normalizedMediaType = mimeType.ToLowerInvariant().Trim();
        var supported = new HashSet<string>
        {
            "application/pdf",
            "text/plain",
            "text/csv",
            "text/markdown",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/msword",
        };
        return supported.Contains(normalizedMediaType);
    }
}