using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;
using Microsoft.EntityFrameworkCore; // For retrieving AiModels
using Microsoft.Extensions.Logging; // Optional logging

namespace Application.Services;

// Define the return type for parallel processing results
public record ParallelAiResponse(Guid ModelId, string Content, int InputTokens, int OutputTokens);

public class ParallelAiProcessingService
{
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly IApplicationDbContext _dbContext; // To fetch AiModel details
    private readonly ILogger<ParallelAiProcessingService>? _logger;

    // Placeholder record for parsed chunk data (same as in MessageStreamer)
    private record ParsedChunk(string? TextDelta, int? InputTokens, int? OutputTokens);

    public ParallelAiProcessingService(
        IAiModelServiceFactory aiModelServiceFactory,
        IAiRequestHandler aiRequestHandler,
        IApplicationDbContext dbContext,
        ILogger<ParallelAiProcessingService>? logger = null)
    {
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _aiRequestHandler = aiRequestHandler ?? throw new ArgumentNullException(nameof(aiRequestHandler));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger;
    }

    // Updated signature to use AiRequestContext
    public async Task<List<ParallelAiResponse>> ProcessInParallelAsync(
        AiRequestContext baseContext, // Contains base session, history, agent, user settings
        IEnumerable<Guid> modelIds,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<ParallelAiResponse?>>();
        var distinctModelIds = modelIds.Distinct().ToList();

        // Fetch all required AiModel objects upfront
        var aiModels = await _dbContext.AiModels
            .Where(m => distinctModelIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m, cancellationToken);

        foreach (var modelId in distinctModelIds)
        {
            if (!aiModels.TryGetValue(modelId, out var specificAiModel))
            {
                _logger?.LogWarning("AI Model with ID {ModelId} not found for parallel processing.", modelId);
                continue; // Skip this model if not found
            }

            // Create a context specific to this model by setting the SpecificModel field
            var specificContext = baseContext with { SpecificModel = specificAiModel };

            // Add a task for processing this model
            tasks.Add(ProcessSingleModelAsync(specificContext, cancellationToken));
        }

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null).ToList()!; // Filter out nulls (errors)
    }

    // Update signature - modelId is now within context.SpecificModel.Id
    private async Task<ParallelAiResponse?> ProcessSingleModelAsync(
        AiRequestContext context, 
        CancellationToken cancellationToken)
    {
        Guid modelId = context.SpecificModel.Id; // Get modelId from the context
        try
        {
            var userId = context.UserId;
            var customApiKey = context.ChatSession.CustomApiKey;
            var aiAgentId = context.ChatSession.AiAgentId;
            var modelType = context.SpecificModel.ModelType; // Use SpecificModel
            
            // 1. Get Service (using modelId from context)
            var aiService = _aiModelServiceFactory.GetService(userId, modelId, customApiKey, aiAgentId);

            // 2. Prepare Payload (using the full context)
            var requestPayload = await _aiRequestHandler.PrepareRequestPayloadAsync(context, cancellationToken);

            // 3. Stream and Process Response
            var responseContent = new StringBuilder();
            int finalInputTokens = 0;
            int finalOutputTokens = 0;

            var rawStream = aiService.StreamResponseAsync(requestPayload, cancellationToken);

            await foreach (var rawChunk in rawStream.WithCancellation(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var parsed = ParseRawChunk(rawChunk.RawContent, modelType); // Pass modelType from SpecificModel
                
                if (!string.IsNullOrEmpty(parsed.TextDelta)) {
                    responseContent.Append(parsed.TextDelta);
                }
                // Update final tokens if reported
                if(parsed.InputTokens.HasValue) finalInputTokens = parsed.InputTokens.Value;
                if(parsed.OutputTokens.HasValue) finalOutputTokens = parsed.OutputTokens.Value;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogInformation("Processing cancelled for model {ModelId}", modelId);
                return null; // Indicate cancellation/failure
            }

            return new ParallelAiResponse(modelId, responseContent.ToString(), finalInputTokens, finalOutputTokens);
        }
        catch (Exception ex)
        { 
             _logger?.LogError(ex, "Error processing model {ModelId} in parallel for User {UserId}", modelId, context.UserId);
             return null; // Return null on error for this specific model
        }
    }
    
    // --- Replicated/Adapted Chunk Parsing Logic --- 
    // Ideally, extract this to a shared service/utility with MessageStreamer

    private ParsedChunk ParseRawChunk(string rawJson, ModelType modelType)
    {
        try
        {
            switch (modelType)
            {
                case ModelType.OpenAi: return ParseOpenAiChunk(rawJson);
                case ModelType.Anthropic: return ParseAnthropicChunk(rawJson);
                case ModelType.Gemini: return ParseGeminiChunk(rawJson);
                case ModelType.DeepSeek: return ParseDeepSeekChunk(rawJson);
                default: _logger?.LogWarning("Parsing not implemented for model type {ModelType}", modelType);
                         return new ParsedChunk(null, null, null);
            }
        }
        catch (JsonException jsonEx) { 
             _logger?.LogError(jsonEx, "(Parallel) Failed to parse AI stream chunk for {ModelType}. RawChunk: {RawChunk}", modelType, rawJson);
             return new ParsedChunk(null, null, null); }
        catch (Exception ex) { 
            _logger?.LogError(ex, "(Parallel) Unexpected error parsing AI stream chunk for {ModelType}. RawChunk: {RawChunk}", modelType, rawJson);
             return new ParsedChunk(null, null, null); }
    }

    private ParsedChunk ParseOpenAiChunk(string rawJson) { /* ... Same as MessageStreamer ... */ 
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null;
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        { text = content.GetString(); }
        return new ParsedChunk(text, null, null);
    }
    private ParsedChunk ParseAnthropicChunk(string rawJson) { /* ... Same as MessageStreamer ... */ 
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null; int? inputTokens = null; int? outputTokens = null;
        if (doc.RootElement.TryGetProperty("type", out var typeElement)) {
           string type = typeElement.GetString() ?? "";
           switch(type) {
               case "content_block_delta": if(doc.RootElement.TryGetProperty("delta", out var delta) && delta.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String) { text = textElement.GetString(); } break;
               case "message_start": if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out var usage) && usage.TryGetProperty("input_tokens", out var iTok) && iTok.ValueKind == JsonValueKind.Number) { inputTokens = iTok.GetInt32(); } break;
               case "message_delta": if (doc.RootElement.TryGetProperty("usage", out var deltaUsage) && deltaUsage.TryGetProperty("output_tokens", out var oTok) && oTok.ValueKind == JsonValueKind.Number) { outputTokens = oTok.GetInt32(); } break;
           }
        }
        return new ParsedChunk(text, inputTokens, outputTokens);
    }
    private ParsedChunk ParseGeminiChunk(string rawJson) { /* ... Same as MessageStreamer ... */ 
         using var doc = JsonDocument.Parse(rawJson);
         string? text = null; int? inputTokens = null; int? outputTokens = null;
         try { if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 && parts[0].TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String) { text = textElement.GetString(); } } catch {} // Ignore potential errors during complex path traversal
         if (doc.RootElement.TryGetProperty("usageMetadata", out var usage)) { 
             if(usage.TryGetProperty("promptTokenCount", out var pToken) && pToken.ValueKind == JsonValueKind.Number) inputTokens = pToken.GetInt32();
             if(usage.TryGetProperty("candidatesTokenCount", out var cToken) && cToken.ValueKind == JsonValueKind.Number) outputTokens = cToken.GetInt32();
         } return new ParsedChunk(text, inputTokens, outputTokens);
    }
    private ParsedChunk ParseDeepSeekChunk(string rawJson) { /* ... Same as MessageStreamer ... */ 
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null; int? inputTokens = null; int? outputTokens = null;
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0) { 
           var firstChoice = choices[0];
           if(firstChoice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) { text = content.GetString(); }
           if (firstChoice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind != JsonValueKind.Null && doc.RootElement.TryGetProperty("usage", out var usage)) { 
                if(usage.TryGetProperty("prompt_tokens", out var pToken) && pToken.ValueKind == JsonValueKind.Number) inputTokens = pToken.GetInt32();
                if(usage.TryGetProperty("completion_tokens", out var cToken) && cToken.ValueKind == JsonValueKind.Number) outputTokens = cToken.GetInt32();
           }
        } return new ParsedChunk(text, inputTokens, outputTokens);
    }
}