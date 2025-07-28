using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using Application.Services.Messaging;
using Application.Services.Helpers;

namespace Infrastructure.Services.AiProvidersServices;

public class QwenService : BaseAiService
{
    private const string QwenBaseUrl = "https://api.aimlapi.com/v1/";
    private readonly ILogger<QwenService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly MultimodalContentParser _multimodalContentParser;

    private static readonly ActivitySource ActivitySource =
        new("Infrastructure.Services.AiProvidersServices.QwenService", "1.0.0");

    protected override string ProviderName => "Qwen";

    public QwenService(
        HttpClient httpClient,
        string? apiKey,
        string modelCode,
        ILogger<QwenService> logger,
        IResilienceService resilienceService,
        IStreamChunkParser chunkParser,
        MultimodalContentParser multimodalContentParser)
        : base(httpClient, apiKey, modelCode, QwenBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        ConfigureHttpClient();
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            activity?.AddEvent(new ActivityEvent("API key not configured."));
            _logger.LogWarning("Qwen (AIMLApi) API key is not configured. Requests will likely fail.");
        }
        else
        {
            HttpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            activity?.SetTag("auth.method", "Bearer");
        }
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting Qwen tool result for ToolCallId {ToolCallId}, ToolName {ToolName}",
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

    protected override string GetEndpointPath() => "chat/completions";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Stream reading cancelled."));
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    activity?.AddEvent(new ActivityEvent("Skipped non-data line in stream",
                        tags: new ActivityTagsCollection
                            { { "line_preview", line.Substring(0, Math.Min(line.Length, 100)) } }));
                }

                continue;
            }

            var jsonData = line.Substring("data:".Length).Trim();
            if (jsonData.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                activity?.AddEvent(new ActivityEvent("Received [DONE] marker."));
                break;
            }

            if (!string.IsNullOrEmpty(jsonData))
            {
                activity?.AddEvent(new ActivityEvent("Yielding data chunk"));
                yield return jsonData;
            }
        }

        activity?.AddEvent(new ActivityEvent("Finished reading stream."));
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
                    if (!attemptRequest.Headers.Accept.Any(h => h.MediaType == "text/event-stream"))
                    {
                        attemptRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    }

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
                ProviderName, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName,
                requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex,
                "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}",
                ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex,
                "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}",
                ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri;
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing cancelled by token.",
                        tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
                    break;
                }

                if (string.IsNullOrWhiteSpace(jsonChunk)) continue;

                var parsedChunk = ChunkParser.ParseChunk(jsonChunk);
                yield return parsedChunk;

                if (parsedChunk.FinishReason is not null)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing completed due to finish_reason."));
                    break;
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Stream completed.",
                    tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
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
        requestObj["stream_options"] = new { include_usage = true };
        AddParameters(requestObj, context);
        CustomizePayload(requestObj, context);
        var processedMessages = await ProcessMessagesForQwenInputAsync(context.History, context.AiAgent,
            context.UserSettings, cancellationToken);
        requestObj["messages"] = processedMessages;
        if (tools != null && tools.Any())
        {
            var formattedTools = tools.Select(def => new
            {
                type = "function",
                function = new
                {
                    name = def.Name,
                    description = def.Description,
                    parameters = def.ParametersSchema
                }
            }).ToList();
            requestObj["tools"] = formattedTools;
            if (!string.IsNullOrEmpty(context.FunctionCall))
            {
                if (context.FunctionCall == "auto")
                {
                    requestObj["tool_choice"] = "auto";
                }
                else if (context.FunctionCall == "none")
                {
                    requestObj["tool_choice"] = "none";
                }
                else
                {
                    requestObj["tool_choice"] = new
                    {
                        type = "function",
                        function = new
                        {
                            name = context.FunctionCall
                        }
                    };
                }
            }
        }

        return new AiRequestPayload(requestObj);
    }

    private async Task<List<object>> ProcessMessagesForQwenInputAsync(
        List<MessageDto> history,
        Domain.Aggregates.AiAgents.AiAgent? aiAgent,
        Domain.Aggregates.Users.UserAiModelSettings? userSettings,
        CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();
        string? systemMessage = aiAgent?.ModelParameter.SystemInstructions ??
                                userSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
            _logger?.LogDebug("Added system instructions as a message for Qwen.");
        }

        foreach (var message in history)
        {
            string role = message.IsFromAi ? "assistant" : "user";
            if (message.FunctionCall != null)
            {
                processedMessages.Add(new
                {
                    role = "assistant",
                    tool_calls = new[]
                    {
                        new
                        {
                            type = "function",
                            function = new
                            {
                                name = message.FunctionCall.Name,
                                arguments = message.FunctionCall.Arguments
                            },
                            id = message.FunctionCall.Id
                        }
                    }
                });
            }
            else if (message.FunctionResponse != null)
            {
                processedMessages.Add(new
                {
                    role = "tool",
                    tool_call_id = message.FunctionResponse.FunctionCallId,
                    name = message.FunctionResponse.Name,
                    content = message.FunctionResponse.Content
                });
            }
            else
            {
                string rawContent = message.Content?.Trim() ?? "";
                if (string.IsNullOrEmpty(rawContent)) continue;
                if (!message.IsFromAi)
                {
                    var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
                    bool hasMultimodalContent = contentParts.Any(p => p is ImagePart || p is FilePart);
                    if (hasMultimodalContent)
                    {
                        var contentArray = new List<object>();
                        foreach (var part in contentParts)
                        {
                            switch (part)
                            {
                                case TextPart textPart:
                                    var txt = textPart.Text?.Trim();
                                    if (!string.IsNullOrEmpty(txt))
                                        contentArray.Add(new { type = "text", text = txt });
                                    break;
                                case ImagePart imagePart:
                                    contentArray.Add(new
                                    {
                                        type = "image_url",
                                        image_url = new
                                        {
                                            url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}"
                                        }
                                    });
                                    break;
                                case FilePart filePart:
                                    if (filePart.MimeType == "text/csv" ||
                                        filePart.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        _logger?.LogWarning(
                                            "CSV file {FileName} detected - Qwen doesn't support CSV files directly. Using the csv_reader plugin is recommended instead.",
                                            filePart.FileName);
                                        contentArray.Add(new
                                        {
                                            type = "text",
                                            text =
                                                $"Note: The CSV file '{filePart.FileName}' can't be processed directly by Qwen. Please use the csv_reader tool to analyze this file. Example usage:\n\n{{\n  \"type\": \"function\",\n  \"function\": {{\n    \"name\": \"csv_reader\",\n    \"arguments\": {{\n      \"file_name\": \"{filePart.FileName}\",\n      \"max_rows\": 100,\n      \"analyze\": true\n    }}\n  }}\n}}"
                                        });
                                    }
                                    else
                                    {
                                        contentArray.Add(new
                                            { type = "text", text = $"[Attached file: {filePart.FileName}]" });
                                    }

                                    break;
                            }
                        }

                        if (contentArray.Count > 0)
                        {
                            processedMessages.Add(new { role = role, content = contentArray });
                        }
                    }
                    else
                    {
                        processedMessages.Add(new { role = role, content = rawContent });
                    }
                }
                else
                {
                    processedMessages.Add(new { role = role, content = rawContent });
                }
            }
        }

        return processedMessages;
    }

    private void AddParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        parameters["temperature"] = context.Temperature;
        parameters["max_tokens"] = context.OutputToken;
        foreach (var kvp in parameters)
        {
            string standardName = kvp.Key;
            string providerName = standardName;
            requestObj[providerName] = kvp.Value;
        }
    }

    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        requestObj["enable_thinking"] = useThinking;
        if (useThinking)
        {
            _logger?.LogDebug("Enabled Qwen native 'enable_thinking' parameter for model {ModelCode}",
                context.SpecificModel.ModelCode);
            if (requestObj.ContainsKey("temperature") && requestObj["temperature"] is double temp && temp < 0.7)
            {
                requestObj["temperature"] = 0.7;
                _logger?.LogDebug("Increased temperature for thinking mode");
            }
        }
    }
}