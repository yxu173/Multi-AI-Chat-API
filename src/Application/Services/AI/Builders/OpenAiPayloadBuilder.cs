using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.Interfaces;
using System.Linq;
using System;

namespace Application.Services.AI.Builders;

public class OpenAiPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public OpenAiPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<OpenAiPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public async Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<PluginDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        AddParameters(requestObj, context);

        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ??
                                context.UserSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            requestObj["instructions"] = systemMessage.Trim();
            Logger?.LogDebug("Adding system instructions for model {ModelCode}", model.ModelCode);
        }

        var processedMessages = await ProcessMessagesForOpenAIInputAsync(context.History, cancellationToken);
        requestObj["input"] = processedMessages;

        if (context.EnableDeepSearch)
        {
            var deepSearchToolName = "jina_deepsearch";
            var deepSearchTool = tools?.FirstOrDefault(t => t.Name == deepSearchToolName);
            if (deepSearchTool != null)
            {
                tools = new List<PluginDefinition> { deepSearchTool };
                requestObj["tool_choice"] = "required";
            }
            else
            {
                Logger?.LogWarning("Deep Search was enabled, but the '{ToolName}' tool was not found or available.", deepSearchToolName);
            }
        }

        if (tools?.Any() == true)
        {
            Logger?.LogInformation("Adding {ToolCount} tool definitions to OpenAI payload for model {ModelCode}",
                tools.Count, model.ModelCode);

            var formattedTools = tools.Select(def =>
            {
                if (def.Name == "code_interpreter")
                {
                    return (object)new {
                        type = "code_interpreter",
                        container = new { type = "auto" }
                    };
                }
                if (def.ParametersSchema is System.Text.Json.Nodes.JsonObject schema &&
                    schema.TryGetPropertyValue("mcp", out var mcpVal) &&
                    mcpVal?.GetValue<bool>() == true)
                {
                    var toolObj = new Dictionary<string, object>
                    {
                        ["type"] = "mcp"
                    };
                    if (schema.TryGetPropertyValue("server_label", out var label) && label is not null)
                        toolObj["server_label"] = label.GetValue<string>();
                    if (schema.TryGetPropertyValue("server_url", out var url) && url is not null)
                        toolObj["server_url"] = url.GetValue<string>();
                    if (schema.TryGetPropertyValue("require_approval", out var approval) && approval is not null)
                        toolObj["require_approval"] = approval.GetValue<string>();
                    if (schema.TryGetPropertyValue("allowed_tools", out var allowed) && allowed is not null)
                        toolObj["allowed_tools"] = allowed;
                    if (schema.TryGetPropertyValue("headers", out var headers) && headers is not null)
                        toolObj["headers"] = headers;
                    // Add more fields as needed
                    return (object)toolObj;
                }
                else
                {
                    return (object)new {
                        type = "function",
                        name = def.Name,
                        description = def.Description,
                        parameters = def.ParametersSchema
                    };
                }
            }).ToList();

            if (formattedTools.Any())
            {
                var firstTool = System.Text.Json.JsonSerializer.Serialize(formattedTools[0]);
                Logger?.LogDebug("First tool structure: {FirstTool}", firstTool);
            }

            requestObj["tools"] = formattedTools;
            
            if (!string.IsNullOrEmpty(context.FunctionCall))
            {
                requestObj["tool_choice"] = context.FunctionCall;
            }
            else 
            {
                requestObj["tool_choice"] = "auto";
            }
        }

        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
        requestObj.Remove("stop");

        CustomizePayload(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private async Task<List<object>> ProcessMessagesForOpenAIInputAsync(List<MessageDto> history, CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();


        foreach (var message in history)
        {
            var role = message.IsFromAi ? "assistant" : "user";
            var rawContent = message.Content?.Trim() ?? "";

            if (string.IsNullOrEmpty(rawContent)) continue;

            if (role == "user")
            {
                var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
                var openAiContentItems = new List<object>();
                bool hasNonTextContent = false;

                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            openAiContentItems.Add(new { type = "input_text", text = textPart.Text });
                            break;
                        case ImagePart imagePart:
                            openAiContentItems.Add(new
                            {
                                type = "input_image",
                                image_url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}"
                            });
                            hasNonTextContent = true;
                            break;
                        case FilePart filePart:
                            if (filePart.MimeType == "text/csv" ||
                                filePart.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                            {
                                Logger?.LogWarning(
                                    "CSV file {FileName} detected - OpenAI doesn't support CSV files directly. " +
                                    "Using the csv_reader plugin is recommended instead.", filePart.FileName);

                                openAiContentItems.Add(new
                                {
                                    type = "input_text",
                                    text =
                                        $"Note: The CSV file '{filePart.FileName}' can't be processed directly by OpenAI. " +
                                        $"Please use the csv_reader tool to analyze this file. Example usage:\n\n" +
                                        $"```json\n{{\n  \"type\": \"function\",\n  \"function\": {{\n    \"name\": \"csv_reader\",\n    \"arguments\": {{\n      \"file_name\": \"{filePart.FileName}\",\n      \"max_rows\": 100,\n      \"analyze\": true\n    }}\n  }}\n}}\n```"
                                });
                            }
                            else
                            {
                                Logger?.LogInformation("Adding file {FileName} using 'input_file' type.",
                                    filePart.FileName);
                                openAiContentItems.Add(new
                                {
                                    type = "input_file",
                                    filename = filePart.FileName,
                                    file_data = $"data:{filePart.MimeType};base64,{filePart.Base64Data}"
                                });
                                hasNonTextContent = true;
                            }

                            break;
                    }
                }

                if (openAiContentItems.Any())
                {
                    if (openAiContentItems.Count == 1 && !hasNonTextContent && openAiContentItems[0] is var textItem &&
                        textItem.GetType().GetProperty("type")?.GetValue(textItem)?.ToString() == "input_text")
                    {
                        string? textContent = textItem.GetType().GetProperty("text")?.GetValue(textItem)?.ToString();
                        if (!string.IsNullOrEmpty(textContent))
                        {
                            processedMessages.Add(new { role = "user", content = textContent });
                        }
                    }
                    else if (openAiContentItems.Count > 0)
                    {
                        processedMessages.Add(new { role = "user", content = openAiContentItems.ToArray() });
                    }
                }

            }
            else
            {
                processedMessages.Add(new { role = "assistant", content = rawContent });
            }
        }

        return processedMessages;
    }

    protected override void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking =
            (context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking);

        if (useEffectiveThinking)
        {
            requestObj["reasoning"] = new { effort = "medium", summary = "detailed" };
            Logger?.LogDebug("Adding reasoning effort for OpenAI model {ModelCode}", context.SpecificModel.ModelCode);

            requestObj.Remove("temperature");
            requestObj.Remove("top_p");
            requestObj.Remove("max_output_tokens");
            requestObj.Remove("max_tokens");

            Logger?.LogDebug("Removed potentially conflicting parameters due to reasoning effort.");
        }
        else if (context.SpecificModel.SupportsThinking)
        {
            Logger?.LogDebug("Model {ModelCode} is marked as supporting thinking but doesn't support reasoning.effort parameter",
                context.SpecificModel.ModelCode);
        }

        if (requestObj.TryGetValue("max_tokens", out var maxTokensValue) && !requestObj.ContainsKey("reasoning"))
        {
            requestObj.Remove("max_tokens");
            if (!requestObj.ContainsKey("max_output_tokens"))
            {
                requestObj["max_output_tokens"] = maxTokensValue;
                Logger?.LogDebug("Mapped 'max_tokens' to 'max_output_tokens'.");
            }
        }
    }

    public static PluginDefinition CreateDeepWikiMcpTool()
    {
        var deepwikiMcpSchema = new System.Text.Json.Nodes.JsonObject
        {
            ["mcp"] = true,
            ["server_label"] = "deepwiki",
            ["server_url"] = "https://mcp.deepwiki.com/mcp",
            ["require_approval"] = "never"
        };
        return new PluginDefinition(
            Guid.Parse("b7e7e7e7-e7e7-4e7e-8e7e-e7e7e7e7e7e7"),
            "deepwiki",
            "DeepWiki MCP server",
            deepwikiMcpSchema
        );
    }
}