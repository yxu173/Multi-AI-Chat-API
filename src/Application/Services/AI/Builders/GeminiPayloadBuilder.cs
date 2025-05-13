using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Application.Services.AI.Interfaces;
using Application.Services.AI.PayloadBuilders;

namespace Application.Services.AI.Builders;

public class GeminiPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;
    private readonly IAiModelServiceFactory _serviceFactory;

    public GeminiPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        IAiModelServiceFactory serviceFactory,
        ILogger<GeminiPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
    }

    public async Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<object>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var generationConfig = new Dictionary<string, object>();
        var safetySettings = GetGeminiSafetySettings(); 

        var parameters = GetMergedParameters(context); 
        ApplyGeminiParametersToConfig(generationConfig, parameters, context.SpecificModel.ModelType); 

        var geminiContents = await ProcessMessagesForGeminiAsync(context, cancellationToken);

        requestObj["contents"] = geminiContents;
        requestObj["generationConfig"] = generationConfig;
        requestObj["safetySettings"] = safetySettings;

        if (tools?.Any() == true && IsParameterSupported("tools", context.SpecificModel.ModelType, isTopLevelParam: true)) // Base method, check top-level
        {
            Logger?.LogInformation("Adding {ToolCount} tool declarations to Gemini payload for model {ModelCode}",
                 tools.Count, context.SpecificModel.ModelCode);
            requestObj["tools"] = new[] { new { functionDeclarations = tools } };
        }
        else
        {
             if (tools?.Any() == true)
             {
                 Logger?.LogWarning("Tools definitions provided but 'tools' parameter not marked as supported for Gemini model {ModelCode}", context.SpecificModel.ModelCode);
             }
        }

        return new AiRequestPayload(requestObj);
    }

    private async Task<List<object>> ProcessMessagesForGeminiAsync(AiRequestContext context, CancellationToken cancellationToken)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ?? context.UserSettings?.ModelParameters.SystemInstructions;
        var geminiContents = new List<object>();

        var systemPrompts = new List<string>();
        if (!string.IsNullOrWhiteSpace(systemMessage)) systemPrompts.Add(systemMessage.Trim());
        if (useEffectiveThinking)
            systemPrompts.Add(
                "When solving complex problems, show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly.");
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
                 Logger?.LogDebug("Injected system/thinking prompt into first user message for Gemini.");
            }
            historyToProcess.Add((role, content));
        }

        if (!systemInjected && !string.IsNullOrWhiteSpace(combinedSystem))
        {
            historyToProcess.Insert(0, ("user", combinedSystem));
            systemInjected = true;
            Logger?.LogWarning("Prepended Gemini system/thinking prompt as initial user message.");
        }

        var mergedHistory = MergeConsecutiveRoles(historyToProcess); 
        IAiFileUploader? fileUploader = null;
        bool needsFileUpload = mergedHistory.Any(m => _multimodalContentParser.Parse(m.Content).Any(p => p is FilePart || p is ImagePart));

        if (needsFileUpload)
        {
            try
            {
                var modelService = _serviceFactory.GetService(context.UserId, context.SpecificModel.Id);
                if (modelService is IAiFileUploader uploader)
                {
                    fileUploader = uploader;
                    Logger?.LogInformation("File uploader service obtained for Gemini: {ServiceType}", modelService.GetType().Name);
                }
                else
                {
                     Logger?.LogWarning("The AI service {ServiceType} for model {ModelCode} does not implement IAiFileUploader. Files cannot be uploaded via API.", modelService.GetType().Name, context.SpecificModel.ModelCode);
                }
            }
            catch (Exception ex)
            {
                 Logger?.LogError(ex, "Error obtaining AI service or file uploader for Gemini model {ModelCode}", context.SpecificModel.ModelCode);
            }
        }

        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = _multimodalContentParser.Parse(rawContent);
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

                    // Check if the file is a CSV, which should be handled by our plugin instead
                    if (part is FilePart && (mimeType == "text/csv" || 
                        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Log that CSV files should be handled by our plugin instead of direct upload
                        Logger?.LogWarning("CSV file {FileName} detected - Using the csv_reader plugin is recommended instead of direct upload", fileName);
                        
                        // Add a text content explaining how to use the plugin instead
                        geminiParts.Add(new { text = $"Note: The CSV file '{fileName}' can be analyzed using the csv_reader plugin. " +
                               $"Example: {{\"name\": \"csv_reader\", \"arguments\": {{\"file_name\": \"{fileName}\", \"analyze\": true}}}}" });
                    }
                    else if (fileUploader != null && IsValidGeminiFileFormat(mimeType)) // Check if format is uploadable
                    {
                        try
                        {
                            byte[] fileBytes = Convert.FromBase64String(base64Data);
                            Logger?.LogInformation("Uploading {PartType} {FileName} ({MimeType}, {Size} bytes) to Gemini File API...", partTypeName, fileName, mimeType, fileBytes.Length);
                            var uploadResult = await fileUploader.UploadFileForAiAsync(fileBytes, mimeType, fileName, cancellationToken);

                            if (uploadResult != null && !string.IsNullOrEmpty(uploadResult.Uri))
                            {
                                geminiParts.Add(new { fileData = new { mimeType = uploadResult.MimeType, fileUri = uploadResult.Uri } });
                                Logger?.LogInformation("{PartType} {FileName} uploaded successfully. URI: {FileUri}", partTypeName, fileName, uploadResult.Uri);
                            }
                            else
                            {
                                Logger?.LogError("{PartType} upload failed for {FileName}: Upload result was null or URI was empty.", partTypeName, fileName);
                                geminiParts.Add(new { text = $"[{partTypeName} Upload Failed: {fileName}]" });
                            }
                        }
                        catch (FormatException ex)
                        {
                            Logger?.LogError(ex, "Invalid Base64 data for {PartType} {FileName}", partTypeName, fileName);
                            geminiParts.Add(new { text = $"[Invalid {partTypeName} Data: {fileName}]" });
                        }
                        catch (Exception ex)
                        {
                            Logger?.LogError(ex, "Error during Gemini file upload processing for {FileName}", fileName);
                            geminiParts.Add(new { text = $"[{partTypeName} Processing Error: {fileName}]" });
                        }
                    }
                    else
                    {
                        string reason = fileUploader == null ? "Uploader N/A" : "Unsupported Format";
                        Logger?.LogWarning("Cannot upload {PartType} {FileName} ({MimeType}) for Gemini. Reason: {Reason}. Sending placeholder text.", partTypeName, fileName, mimeType, reason);
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

    private void ApplyGeminiParametersToConfig(Dictionary<string, object> generationConfig, Dictionary<string, object> parameters, ModelType modelType)
    {
        foreach (var kvp in parameters)
        {
            string geminiName = GetProviderParameterName(kvp.Key, modelType); 
            if (IsParameterSupported(geminiName, modelType, isTopLevelParam: false))
            {
                generationConfig[geminiName] = kvp.Value;
                 Logger?.LogDebug("Applied parameter {Key} as {GeminiName} to Gemini generationConfig", kvp.Key, geminiName);
            }
            else
            {
                 if (!IsParameterSupported(geminiName, modelType, isTopLevelParam: true))
                 {
                     Logger?.LogDebug("Skipping unsupported or non-GenerationConfig parameter '{StandardName}' (mapped to '{GeminiName}') for Gemini", kvp.Key, geminiName);
                 }
            }
        }
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
            // Images
            "image/png", "image/jpeg", "image/webp", "image/heic", "image/heif",
            // Audio
            "audio/wav", "audio/mp3", "audio/aiff", "audio/aac", "audio/ogg", "audio/flac",
            // Video
             "video/mp4", "video/mpeg", "video/mov", "video/avi", "video/flv", "video/wmv", "video/webm", "video/h264", "video/3gpp",
             // Text/Code (though typically sent inline)
             "text/plain", "text/html", "text/css", "text/javascript", "application/json", "application/xml",
             "text/markdown", "text/csv", "text/rtf", 
             "text/x-python", "application/x-python-code",
             "text/x-java-source", "text/x-c", "text/x-c++", "text/x-csharp", "text/x-php", "text/x-ruby",
             "text/x-swift", "text/x-go", "text/x-kotlin", "text/x-typescript",
             // Documents (Less explicitly listed, but often work)
              "application/pdf",
               "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // docx
              "application/msword", // doc
               "application/vnd.openxmlformats-officedocument.presentationml.presentation", // pptx
              "application/vnd.ms-powerpoint", // ppt
               "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", // xlsx
              "application/vnd.ms-excel" // xls
        };
        
        bool isValid = supportedMimeTypes.Contains(mimeType);
        if (!isValid)
        {
             Logger?.LogWarning("Mime type '{MimeType}' is not explicitly listed as supported for Gemini File API uploads.", mimeType);
        }
        return isValid; 
    }

    protected new bool IsParameterSupported(string providerParamName, ModelType modelType, bool isTopLevelParam = false)
    {
        if (modelType != ModelType.Gemini) {
             return base.IsParameterSupported(providerParamName, modelType);
        }

        if (isTopLevelParam)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                 "contents", "tools", "safetySettings", "generationConfig" 
            }.Contains(providerParamName);
        }
        else
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "temperature", "topP", "topK", "maxOutputTokens", "stopSequences", "candidateCount",
                "response_mime_type", "response_schema"
            }.Contains(providerParamName);
        }
    }
} 