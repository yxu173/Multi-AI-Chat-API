using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Diagnostics;
using Application.Services.Messaging;
using Application.Services.Helpers;

namespace Infrastructure.Services.AiProvidersServices;

public class GeminiService : BaseAiService, IAiFileUploader
{
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/";
    private readonly ILogger<GeminiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly MultimodalContentParser _multimodalContentParser;
    private readonly IAiModelServiceFactory _serviceFactory;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.GeminiService", "1.0.0");

    public GeminiService(
        HttpClient httpClient,
        string? apiKey, 
        string modelCode, 
        ILogger<GeminiService> logger,
        IResilienceService resilienceService,
        IStreamChunkParser chunkParser,
        MultimodalContentParser multimodalContentParser,
        IAiModelServiceFactory serviceFactory)
        : base(httpClient, apiKey, modelCode, GeminiBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        ConfigureHttpClient();
    }

    protected override string ProviderName => "Gemini";

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting Gemini tool result for ToolCallId {ToolCallId}, ToolName {ToolName}", context.ToolCallId, context.ToolName);

        var messagePayload = new
        {
            parts = new[]
            {
                new
                {
                    functionResponse = new
                    {
                        name = context.ToolName,
                        response = new
                        {
                            content = TryParseJsonElement(context.Result) ?? (object)context.Result
                        }
                    }
                }
            }
        };
        
        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        var messageDto = new MessageDto(contentJson, false, Guid.NewGuid());
        
        return Task.FromResult(messageDto);
    }
    
    private JsonElement? TryParseJsonElement(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Tool result content was not valid JSON: {Content}", jsonString);
            return null;
        }
    }

    protected override string GetEndpointPath() => $"models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
        
        await foreach (var jsonElement in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Gemini stream reading cancelled."));
                break;
            }
            string rawJsonChunk = jsonElement.GetRawText();
            activity?.AddEvent(new ActivityEvent("Gemini stream chunk received"));
            yield return rawJsonChunk;
        }
    }

    public async Task<AiFileUploadResult?> UploadFileForAiAsync(byte[] fileBytes, string mimeType, string fileName, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(UploadFileForAiAsync));
        activity?.SetTag("ai.provider", ProviderName);
        activity?.SetTag("file.name", fileName);
        activity?.SetTag("file.mime_type", mimeType);
        activity?.SetTag("file.size_bytes", fileBytes.Length);

        var uploadUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={ApiKey}";
        activity?.SetTag("http.url_template", "https://generativelanguage.googleapis.com/upload/v1beta/files?key=API_KEY_REDACTED");
        _logger.LogInformation("Preparing to upload file to {ProviderName}: {FileName}, MIME: {MimeType}, Size: {Size} bytes, using resilience pipeline", ProviderName, fileName, mimeType, fileBytes.Length);

        HttpResponseMessage? uploadResponse = null;
        Uri? requestUriForLogging = null; 

        try
        {
            uploadResponse = await _resiliencePipeline.ExecuteAsync(
                async ct => 
                {
                    using var attemptActivity = ActivitySource.StartActivity("UploadFileAttempt");
                    var attemptRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                    attemptRequest.Headers.Add("X-Goog-Upload-Protocol", "raw");
                    // For ByteArrayContent, it's generally safe to reuse the same byte array.
                    // If issues were to arise with content being "consumed", it would need to be new byte[fileBytes.Length] and fileBytes.CopyTo(newArray,0)
                    // but ByteArrayContent itself can typically be reused if the underlying array isn't modified.
                    attemptRequest.Content = new ByteArrayContent(fileBytes); 
                    attemptRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                    
                    requestUriForLogging = attemptRequest.RequestUri;
                    attemptActivity?.SetTag("http.url", requestUriForLogging?.ToString());
                    attemptActivity?.SetTag("http.method", HttpMethod.Post.ToString());
                    _logger.LogDebug("Attempting to upload file {FileName} to {ProviderName}: {Endpoint} via Polly pipeline", fileName, ProviderName, requestUriForLogging);
                    return await HttpClient.SendAsync(attemptRequest, ct);
                },
                cancellationToken).ConfigureAwait(false);
            
            activity?.SetTag("http.response_status_code", ((int)uploadResponse.StatusCode).ToString());
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open for file upload.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName} file upload. Request for {FileName} to {Uri} was not sent.", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "File upload timed out by Polly.");
            activity?.AddException(ex);
            _logger.LogError(ex, "{ProviderName} file upload request for {FileName} to {Uri} timed out.", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "HTTP request exception during file upload.");
            activity?.AddException(ex);
            _logger.LogError(ex, "HTTP request failed during {ProviderName} file upload for {FileName}. URI: {Uri}", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw; 
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "File upload cancelled by user.");
            activity?.AddEvent(new ActivityEvent("File upload cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} file upload cancelled for {FileName}. URI: {Uri}", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }

        if (uploadResponse == null) 
        {
            var errorMsg = $"{ProviderName} file upload response was null after resilience pipeline execution for {fileName}.";
            activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
            _logger.LogError(errorMsg);
            throw new InvalidOperationException($"Upload response was null for {fileName} with {ProviderName}.");
        }

        if (!uploadResponse.IsSuccessStatusCode)
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"File upload failed with status {uploadResponse.StatusCode}");
            _logger.LogWarning("{ProviderName} file upload failed for {FileName} with status {StatusCode}. URI: {Uri}", ProviderName, fileName, uploadResponse.StatusCode, uploadResponse.RequestMessage?.RequestUri);
            await HandleApiErrorAsync(uploadResponse, providerApiKeyId: null).ConfigureAwait(false); 
            return null;
        }

        // 2. Extract file metadata from the response
        string uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        activity?.AddEvent(new ActivityEvent("File upload response received."));
        _logger.LogDebug("Gemini file upload response for {FileName}: {ResponseBody}", fileName, uploadResponseBody);
        
        using var jsonDoc = JsonDocument.Parse(uploadResponseBody);

        if (!jsonDoc.RootElement.TryGetProperty("file", out var fileElement) ||
            !fileElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("mimeType", out var mimeTypeElement) || mimeTypeElement.ValueKind != JsonValueKind.String)
        {
            var errorMsg = "Could not parse file metadata from Gemini upload response.";
            activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
            _logger.LogError("{ErrorDetails} for {FileName}: {ResponseBody}", errorMsg, fileName, uploadResponseBody);
            throw new InvalidOperationException("Failed to parse Gemini file upload response.");
        }

        long sizeBytes = fileElement.TryGetProperty("sizeBytes", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : 0;

        var result = new AiFileUploadResult(
            ProviderFileId: nameElement.GetString()!,
            Uri: uriElement.GetString()!,
            MimeType: mimeTypeElement.GetString()!,
            SizeBytes: sizeBytes,
            OriginalFileName: fileName
        );
        activity?.SetTag("ai.file.provider_id", result.ProviderFileId);
        activity?.SetTag("ai.file.uri", result.Uri);
        _logger.LogInformation("Successfully uploaded file {FileName} to {ProviderName}. ProviderFileId: {ProviderFileId}", fileName, ProviderName, result.ProviderFileId);
        return result;
    }

    public override async Task<AiRequestPayload> BuildPayloadAsync(
        AiRequestContext context,
        List<PluginDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var generationConfig = new Dictionary<string, object>();
        var safetySettings = GetGeminiSafetySettings();
        AddParameters(generationConfig, context); 
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        if (useThinking)
        {
            int thinkingBudget = -1;
            generationConfig["thinkingConfig"] = new Dictionary<string, object>
            {
                { "thinkingBudget", thinkingBudget },
                {"includeThoughts",true}
            };
        }
        var geminiContents = await ProcessMessagesForGeminiAsync(context, cancellationToken);
        requestObj["contents"] = geminiContents;
        requestObj["generationConfig"] = generationConfig;
        requestObj["safetySettings"] = safetySettings;
        if ( tools?.Any() == true)
        {
            _logger?.LogInformation("Adding {ToolCount} tool declarations to Gemini payload for model {ModelCode}",
                 tools.Count, context.SpecificModel.ModelCode);
            requestObj["tools"] = new[] { new { functionDeclarations = TransformToolsForGemini(tools) } };
        }
        return new AiRequestPayload(requestObj);
    }

    private List<object> TransformToolsForGemini(List<PluginDefinition> tools)
    {
        return tools.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parameters = t.ParametersSchema
        }).Cast<object>().ToList();
    }

    private async Task<List<object>> ProcessMessagesForGeminiAsync(AiRequestContext context, CancellationToken cancellationToken)
    {
        string? systemMessage = null;
        if (context.AiAgent != null && context.AiAgent.ModelParameter != null)
        {
            systemMessage = context.AiAgent.ModelParameter.SystemInstructions;
        }
        else if (context.UserSettings != null && context.UserSettings.ModelParameters != null)
        {
            systemMessage = context.UserSettings.ModelParameters.SystemInstructions;
        }
        var geminiContents = new List<object>();
        var systemPrompts = new List<string>();
        if (!string.IsNullOrWhiteSpace(systemMessage)) systemPrompts.Add(systemMessage.Trim());
        string combinedSystem = string.Join("\n\n", systemPrompts);
        var historyToProcess = new List<(string Role, string Content)>();
        bool systemInjected = false;
        foreach (var msg in context.History)
        {
            string role = msg.IsFromAi ? "model" : "user"; 
            string content = msg.Content?.Trim() ?? "";
            if (!systemInjected && role == "user" && !string.IsNullOrWhiteSpace(combinedSystem))
            {
                content = $"{combinedSystem}\n\n{content}";
                systemInjected = true;
                 _logger?.LogDebug("Injected system/thinking prompt into first user message for Gemini.");
            }
            historyToProcess.Add((role, content));
        }
        if (!systemInjected && !string.IsNullOrWhiteSpace(combinedSystem))
        {
            historyToProcess.Insert(0, ("user", combinedSystem));
            _logger?.LogWarning("Prepended Gemini system/thinking prompt as initial user message.");
        }
        var mergedHistory = MergeConsecutiveRoles(historyToProcess); 
        IAiFileUploader? fileUploader = null;
        bool needsFileUpload = false;
        foreach (var msg in mergedHistory)
        {
            var tempContentParts = await _multimodalContentParser.ParseAsync(msg.Content, cancellationToken); 
            if (tempContentParts.Any(p => p is FilePart || p is ImagePart))
            {
                needsFileUpload = true;
                break;
            }
        }
        if (needsFileUpload)
        {
            try
            {
                var modelService = _serviceFactory.GetService(context.UserId, context.SpecificModel.Id);
                if (modelService is IAiFileUploader uploader)
                {
                    fileUploader = uploader;
                    _logger?.LogInformation("File uploader service obtained for Gemini: {ServiceType}", modelService.GetType().Name);
                }
                else
                {
                     _logger?.LogWarning("The AI service {ServiceType} for model {ModelCode} does not implement IAiFileUploader. Files cannot be uploaded via API.", modelService.GetType().Name, context.SpecificModel.ModelCode);
                }
            }
            catch (Exception ex)
            {
                 _logger?.LogError(ex, "Error obtaining AI service or file uploader for Gemini model {ModelCode}", context.SpecificModel.ModelCode);
            }
        }
        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;
            var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
            var geminiParts = new List<object>();
            foreach (var part in contentParts)
            {
                if (part is TextPart tp)
                {
                    geminiParts.Add(new { text = tp.Text });
                }
                else if (part is ImagePart ip || part is FilePart fp)
                {
                    string fileName = (part is FilePart fileP) ? fileP.FileName : ((ImagePart)part).FileName ?? "image.tmp";
                    string mimeType = (part is FilePart fileP2) ? fileP2.MimeType : ((ImagePart)part).MimeType;
                    string base64Data = (part is FilePart fileP3) ? fileP3.Base64Data : ((ImagePart)part).Base64Data;
                    string partTypeName = part.GetType().Name.Replace("Part", "");
                    if (part is FilePart && (mimeType == "text/csv" || 
                        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger?.LogWarning("CSV file {FileName} detected - Using the csv_reader plugin is recommended instead of direct upload", fileName);
                        geminiParts.Add(new { text = $"Note: The CSV file '{fileName}' can be analyzed using the csv_reader plugin. " +
                               $"Example: {{\"name\": \"csv_reader\", \"arguments\": {{\"file_name\": \"{fileName}\", \"analyze\": true}}}}" });
                    }
                    else if (fileUploader != null && IsValidGeminiFileFormat(mimeType))
                    {
                        try
                        {
                            byte[] fileBytes = Convert.FromBase64String(base64Data);
                            _logger?.LogInformation("Uploading {PartType} {FileName} ({MimeType}, {Size} bytes) to Gemini File API...", partTypeName, fileName, mimeType, fileBytes.Length);
                            var uploadResult = await fileUploader.UploadFileForAiAsync(fileBytes, mimeType, fileName, cancellationToken);
                            if (uploadResult != null && !string.IsNullOrEmpty(uploadResult.Uri))
                            {
                                geminiParts.Add(new { fileData = new { mimeType = uploadResult.MimeType, fileUri = uploadResult.Uri } });
                                _logger?.LogInformation("{PartType} {FileName} uploaded successfully. URI: {FileUri}", partTypeName, fileName, uploadResult.Uri);
                            }
                            else
                            {
                                _logger?.LogError("{PartType} upload failed for {FileName}: Upload result was null or URI was empty.", partTypeName, fileName);
                                geminiParts.Add(new { text = $"[{partTypeName} Upload Failed: {fileName}]" });
                            }
                        }
                        catch (FormatException ex)
                        {
                            _logger?.LogError(ex, "Invalid Base64 data for {PartType} {FileName}", partTypeName, fileName);
                            geminiParts.Add(new { text = $"[Invalid {partTypeName} Data: {fileName}]" });
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error during Gemini file upload processing for {FileName}", fileName);
                            geminiParts.Add(new { text = $"[{partTypeName} Processing Error: {fileName}]" });
                        }
                    }
                    else
                    {
                        string reason = fileUploader == null ? "Uploader N/A" : "Unsupported Format";
                        _logger?.LogWarning("Cannot upload {PartType} {FileName} ({MimeType}) for Gemini. Reason: {Reason}. Sending placeholder text.", partTypeName, fileName, mimeType, reason);
                        geminiParts.Add(new { text = $"[Attached {partTypeName}: {fileName} - {reason}]" });
                    }
                }
            }
            if (geminiParts.Any())
            {
                geminiContents.Add(new { role = role, parts = geminiParts.ToArray() });
            }
        }
        EnsureAlternatingRoles(geminiContents, "user", "model"); 
        return geminiContents;
    }

    private List<object> GetGeminiSafetySettings()
    {
        return new List<object>
        {
            new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
        };
    }

    private bool IsValidGeminiFileFormat(string mimeType)
    {
        var supportedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/png", "image/jpeg", "image/webp", "image/heic", "image/heif",
            "audio/wav", "audio/mp3", "audio/aiff", "audio/aac", "audio/ogg", "audio/flac",
             "video/mp4", "video/mpeg", "video/mov", "video/avi", "video/flv", "video/wmv", "video/webm", "video/h264", "video/3gpp",
             "text/plain", "text/html", "text/css", "text/javascript", "application/json", "application/xml",
             "text/markdown", "text/csv", "text/rtf", 
             "text/x-python", "application/x-python-code",
             "text/x-java-source", "text/x-c", "text/x-c++", "text/x-csharp", "text/x-php", "text/x-ruby",
             "text/x-swift", "text/x-go", "text/x-kotlin", "text/x-typescript",
              "application/pdf",
               "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
              "application/msword",
               "application/vnd.openxmlformats-officedocument.presentationml.presentation",
              "application/vnd.ms-powerpoint",
               "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
              "application/vnd.ms-excel"
        };
        bool isValid = supportedMimeTypes.Contains(mimeType);
        if (!isValid)
        {
             _logger?.LogWarning("Mime type '{MimeType}' is not explicitly listed as supported for Gemini File API uploads.", mimeType);
        }
        return isValid; 
    }

    private void AddParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        var model = context.SpecificModel;
        var agent = context.AiAgent;
        var userSettings = context.UserSettings;
        if (agent?.AssignCustomModelParameters == true && agent.ModelParameter != null)
        {
            var sourceParams = agent.ModelParameter;
            parameters["temperature"] = sourceParams.Temperature;
            parameters["max_tokens"] = sourceParams.MaxTokens;
        }
        else if (userSettings != null)
        {
            parameters["temperature"] = userSettings.ModelParameters.Temperature;
        }
        if (!parameters.ContainsKey("max_tokens") && model.MaxOutputTokens.HasValue)
        {
            parameters["max_tokens"] = model.MaxOutputTokens.Value;
        }
        foreach (var kvp in parameters)
        {
            string standardName = kvp.Key;
            string providerName = standardName;
            requestObj[providerName] = kvp.Value;
        }
    }

    private List<(string Role, string Content)> MergeConsecutiveRoles(List<(string Role, string Content)> messages)
    {
        if (messages == null || !messages.Any()) return new List<(string Role, string Content)>();
        var merged = new List<(string Role, string Content)>();
        var currentRole = messages[0].Role;
        var currentContent = new System.Text.StringBuilder(messages[0].Content);
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

    private void EnsureAlternatingRoles(List<object> messages, string userRole, string modelRole)
    {
        if (messages == null || !messages.Any()) return;
        string firstRole = GetRoleFromDynamicMessage(messages[0]);
        if (firstRole != userRole)
        {
            _logger?.LogError(
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
                    _logger?.LogWarning(
                        "Found consecutive '{CurrentRole}' roles at index {Index} for {ModelRole} provider.",
                        currentRole, i, modelRole);
                    bool handled = false;
                    if (modelRole == "model" && currentRole == userRole)
                    {
                        _logger?.LogWarning(
                            "Injecting placeholder '{ModelRole}' message to fix consecutive '{UserRole}' roles for Gemini.",
                            modelRole, userRole);
                        cleanedMessages.Add(new { role = modelRole, parts = new[] { new { text = "..." } } });
                        cleanedMessages.Add(messages[i]);
                        handled = true;
                    }
                    if (!handled)
                    {
                        _logger?.LogError(
                            "Unhandled consecutive '{CurrentRole}' role at index {Index} for {ModelRole}. Skipping message to avoid potential API error. Original Message: {OriginalMessage}",
                            currentRole, i, modelRole, TrySerialize(messages[i]));
                    }
                }
            }
        }
        messages.Clear();
        messages.AddRange(cleanedMessages);
    }

    private string GetRoleFromDynamicMessage(dynamic message)
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
        catch (Exception) { }
        return string.Empty;
    }

    private string TrySerialize(object obj)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            });
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}] - Type: {obj?.GetType().Name ?? "null"}";
        }
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
        _logger.LogInformation("Preparing to send request to {ProviderName} model {ModelCode} with API Key ID (if managed): {ApiKeyId} using resilience pipeline", 
            ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");

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
                    _logger.LogDebug("Attempting to send request to {ProviderName} endpoint: {Endpoint} via Polly pipeline", ProviderName, requestUriForLogging);
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
            _logger.LogDebug("Successfully received stream response header from {ProviderName} model {ModelCode}", ProviderName, ModelCode);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName} stream. Request to {Uri} was not sent.", ProviderName, requestUriForLogging ?? new Uri(GeminiBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} stream timed out. URI: {Uri}", ProviderName, requestUriForLogging ?? new Uri(GeminiBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(GeminiBaseUrl + GetEndpointPath()));
            yield break; 
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(GeminiBaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri;
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing cancelled by token.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
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
                activity?.AddEvent(new ActivityEvent("Stream completed.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}