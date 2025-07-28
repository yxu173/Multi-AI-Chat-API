using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Diagnostics;
using Application.Services.AI.Streaming;
using System.Text.Json;
using Application.Services.Helpers;
using Application.Services.Messaging;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : BaseAiService
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1/";
    private readonly ILogger<OpenAiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly MultimodalContentParser _multimodalContentParser;

    private static readonly ActivitySource ActivitySource =
        new("Infrastructure.Services.AiProvidersServices.OpenAiService", "1.0.0");

    protected override string ProviderName => "OpenAI";

    public OpenAiService(
        HttpClient httpClient,
        string? apiKey,
        string modelCode,
        ILogger<OpenAiService> logger,
        IResilienceService resilienceService,
        OpenAiStreamChunkParser chunkParser,
        MultimodalContentParser multimodalContentParser,
        TimeSpan? timeout = null)
        : base(httpClient, apiKey, modelCode, OpenAiBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName, timeout)
                              ?? throw new ArgumentNullException(nameof(resilienceService));
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        if (timeout.HasValue)
        {
            HttpClient.Timeout = timeout.Value;
        }

        ConfigureHttpClient();
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        HttpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
        }

        HttpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");
    }

    protected override string GetEndpointPath()
    {
        return "responses";
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting OpenAI tool result for ToolCallId {ToolCallId}, ToolName {ToolName}",
            context.ToolCallId, context.ToolName);

        var messagePayload = new
        {
            role = "tool",
            tool_call_id = context.ToolCallId,
            content = context.Result
        };

        string contentJson =
            JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        var messageDto = new MessageDto(contentJson, false, Guid.NewGuid());

        return Task.FromResult(messageDto);
    }

    public override async IAsyncEnumerable<ParsedChunkInfo> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        using var activity = ActivitySource.StartActivity(nameof(StreamResponseAsync));
        activity?.SetTag("ai.provider", ProviderName);
        activity?.SetTag("ai.model", ModelCode);
        activity?.SetTag("ai.provider_api_key_id", providerApiKeyId?.ToString());

        HttpResponseMessage? response = null;
        Uri? requestUriForLogging = null;

        try
        {
            response = await _resiliencePipeline.ExecuteAsync(
                async ct =>
                {
                    using var attemptActivity = ActivitySource.StartActivity("SendHttpRequestAttempt");
                    var attemptRequest = CreateRequest(requestPayload);
                    requestUriForLogging = attemptRequest.RequestUri;
                    attemptActivity?.SetTag("http.url", requestUriForLogging?.ToString());
                    attemptActivity?.SetTag("http.method", attemptRequest.Method.ToString());

                    return await HttpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken).ConfigureAwait(false);

            activity?.SetTag("http.response_status_code", ((int)response.StatusCode).ToString());

            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"API call failed with status {response.StatusCode}");
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.",
                ProviderName, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName,
                requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex,
                "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName,
                ModelCode, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex,
                "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}",
                ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw;
        }

        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(jsonChunk)) continue;

                if (jsonChunk == "[DONE]")
                {
                    activity?.AddEvent(new ActivityEvent("Stream finished with [DONE] marker."));
                    break;
                }

                yield return ChunkParser.ParseChunk(jsonChunk);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Finished reading stream successfully."));
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    public override async Task<AiRequestPayload> BuildPayloadAsync(AiRequestContext context,
        List<PluginDefinition>? tools = null, CancellationToken cancellationToken = default)
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
            _logger?.LogDebug("Adding system instructions for model {ModelCode}", model.ModelCode);
        }

        var processedMessages = await ProcessMessagesForOpenAIInputAsync(context.History, cancellationToken);
        requestObj["input"] = processedMessages;

        if (tools?.Any() == true)
        {
            _logger?.LogInformation("Adding {ToolCount} tool definitions to OpenAI payload for model {ModelCode}",
                tools.Count, model.ModelCode);

            var formattedTools = tools.Select(def =>
            {
                if (def.Name == "code_interpreter")
                {
                    return (object)new
                    {
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
                    return (object)toolObj;
                }
                else
                {
                    return (object)new
                    {
                        type = "function",
                        name = def.Name,
                        description = def.Description,
                        parameters = def.ParametersSchema
                    };
                }
            }).ToList();

            if (formattedTools.Any())
            {
                var firstTool = JsonSerializer.Serialize(formattedTools[0]);
                _logger?.LogDebug("First tool structure: {FirstTool}", firstTool);
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

    public Task<AiRequestPayload> BuildDeepResearchPayloadAsync(AiRequestContext context,
        List<PluginDefinition>? tools = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Preparing payload for OpenAI Deep Research model {ModelCode}",
            context.SpecificModel.ModelCode);

        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;
        requestObj["background"] = true;
        requestObj["model"] = model.ModelCode;

        var lastUserMessage = context.History.LastOrDefault(m => !m.IsFromAi && !string.IsNullOrWhiteSpace(m.Content));
        if (lastUserMessage is not null)
        {
            requestObj["input"] = lastUserMessage.Content!.Trim();
        }
        else
        {
            _logger.LogWarning(
                "No user message found in history for deep research request for ChatSession {ChatSessionId}",
                context.ChatSession.Id);
            requestObj["input"] = "";
        }

        requestObj["stream"] = true;

        var deepResearchTools = new List<object>();
        deepResearchTools.Add(new { type = "web_search_preview" });
        _logger.LogInformation("Enabling 'web_search_preview' tool for deep research.");

        if (tools?.Any(t => t.Name.Equals("code_interpreter", StringComparison.OrdinalIgnoreCase)) == true)
        {
            deepResearchTools.Add(new { type = "code_interpreter", container = new { type = "auto" } });
            _logger.LogInformation("Enabling 'code_interpreter' tool for deep research.");
        }

        if (deepResearchTools.Any())
        {
            requestObj["tools"] = deepResearchTools;
        }
        else
        {
            _logger.LogWarning(
                "No tools specified for deep research model {ModelCode}. The API may reject this request as it requires at least one data source.",
                model.ModelCode);
        }

        return Task.FromResult(new AiRequestPayload(requestObj));
    }

    private async Task<List<object>> ProcessMessagesForOpenAIInputAsync(List<MessageDto> history,
        CancellationToken cancellationToken)
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
                                _logger?.LogWarning(
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
                                _logger?.LogInformation("Adding file {FileName} using 'input_file' type.",
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

    private void AddParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        var model = context.SpecificModel;
        var agent = context.AiAgent;
        var userSettings = context.UserSettings;


        parameters["temperature"] = context.Temperature;
        parameters["max_output_tokens"] = context.OutputToken;


        foreach (var kvp in parameters)
        {
            string standardName = kvp.Key;
            string providerName = standardName;
            // OpenAI uses standard names, so no mapping needed
            requestObj[providerName] = kvp.Value;
        }
    }

    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking =
            (context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking);
        if (useEffectiveThinking)
        {
            requestObj["reasoning"] = new { effort = "medium", summary = "detailed" };
            _logger?.LogDebug("Adding reasoning effort for OpenAI model {ModelCode}", context.SpecificModel.ModelCode);
            requestObj.Remove("temperature");
            requestObj.Remove("top_p");
            requestObj.Remove("max_output_tokens");
            requestObj.Remove("max_tokens");
            _logger?.LogDebug("Removed potentially conflicting parameters due to reasoning effort.");
        }
        else if (context.SpecificModel.SupportsThinking)
        {
            _logger?.LogDebug(
                "Model {ModelCode} is marked as supporting thinking but doesn't support reasoning.effort parameter",
                context.SpecificModel.ModelCode);
        }

        if (requestObj.TryGetValue("max_tokens", out var maxTokensValue) && !requestObj.ContainsKey("reasoning"))
        {
            requestObj.Remove("max_tokens");
            if (!requestObj.ContainsKey("max_output_tokens"))
            {
                requestObj["max_output_tokens"] = maxTokensValue;
                _logger?.LogDebug("Mapped 'max_tokens' to 'max_output_tokens'.");
            }
        }
    }
}