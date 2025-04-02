using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Domain.Enums;
using Domain.Common;

namespace Application.Services;

/// <summary>
/// Manages the streaming of AI responses and related operations like token updates and notifications.
/// </summary>
public class MessageStreamer
{
    private readonly IMediator _mediator;
    private readonly IMessageRepository _messageRepository;
    private readonly TokenUsageService _tokenUsageService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly ILogger<MessageStreamer> _logger;
    private readonly IAiRequestHandler _aiRequestHandler;

    public MessageStreamer(
        IMediator mediator,
        IMessageRepository messageRepository,
        TokenUsageService tokenUsageService,
        StreamingOperationManager streamingOperationManager,
        ILogger<MessageStreamer> logger,
        IAiRequestHandler aiRequestHandler)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
        _streamingOperationManager = streamingOperationManager ?? throw new ArgumentNullException(nameof(streamingOperationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiRequestHandler = aiRequestHandler ?? throw new ArgumentNullException(nameof(aiRequestHandler));
    }

    /// <summary>
    /// Handles the end-to-end process of streaming an AI response for a given chat context.
    /// </summary>
    public async Task StreamResponseAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken)
    {
        var chatSessionId = requestContext.ChatSession.Id;
        var aiModel = requestContext.ChatSession.AiModel;
        var modelType = aiModel.ModelType;
        var supportsThinking = aiModel.SupportsThinking;
        
        var cts = new CancellationTokenSource();
        _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);
        
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var linkedToken = linkedCts.Token;

        int finalInputTokens = 0;
        int finalOutputTokens = 0;

        try
        {
            _logger.LogInformation("Preparing request payload for model {ModelType}", modelType);
            var requestPayload = await _aiRequestHandler.PrepareRequestPayloadAsync(requestContext, linkedToken);
            
            _logger.LogInformation("Initiating AI stream request for message {MessageId}", aiMessage.Id);
            var rawStream = aiService.StreamResponseAsync(requestPayload, linkedToken);

            (finalInputTokens, finalOutputTokens) = await ProcessAiStreamAsync(
                rawStream, 
                modelType, 
                supportsThinking, 
                aiMessage, 
                chatSessionId, 
                linkedToken);

            if (!linkedToken.IsCancellationRequested && aiMessage.Status != MessageStatus.Interrupted) {
                aiMessage.CompleteMessage();
            }
            var finalUpdateToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            await _messageRepository.UpdateAsync(aiMessage, finalUpdateToken);
            
            if (!linkedToken.IsCancellationRequested && aiMessage.Status == MessageStatus.Completed)
            {
                 await _mediator.Publish(new ResponseCompletedNotification(chatSessionId, aiMessage.Id), finalUpdateToken);
            }
            
            await FinalizeTokenUsage(chatSessionId, aiModel, finalInputTokens, finalOutputTokens, finalUpdateToken);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
             bool userCancelled = cancellationToken.IsCancellationRequested;
             _logger.LogInformation("Streaming operation cancelled for chat session {ChatSessionId}. Reason: {Reason}", 
                 chatSessionId, userCancelled ? "User Request" : "Internal Stop");
                 
             var stopReason = userCancelled ? "Cancelled by user" : "Stopped internally";
             await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);
             
             if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted)
             {
                 aiMessage.AppendContent($"\n[{stopReason}]");
                 aiMessage.InterruptMessage(); 
                 await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None); 
             }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI response streaming for chat session {ChatSessionId}.", chatSessionId);
            if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted) {
                 aiMessage.AppendContent($"\n[Error: {ex.Message}]");
                 aiMessage.InterruptMessage(); 
                 await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
            }
            await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);
        }
        finally
        { 
            _streamingOperationManager.StopStreaming(aiMessage.Id);
            cts.Dispose();
            _logger.LogInformation("Streaming operation finished or cleaned up for chat session {ChatSessionId}", chatSessionId);
        }
    }

    private async Task<(int FinalInputTokens, int FinalOutputTokens)> ProcessAiStreamAsync(
        IAsyncEnumerable<AiRawStreamChunk> rawStream,
        ModelType modelType,
        bool supportsThinking,
        Message aiMessage,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        var accumulatedContent = new StringBuilder();
        var currentThinkingContent = new StringBuilder();
        bool isInThinkingBlock = false;
        string thinkingMarker = "### Thinking:";
        string answerMarker = "### Answer:";
        
        int latestReportedInputTokens = 0;
        int latestReportedOutputTokens = 0;
        bool finalTokensReported = false;

        // Track the type of the current content block being processed
        string? currentBlockType = null; // "text" or "thinking"

        await foreach (var rawChunk in rawStream.WithCancellation(cancellationToken))
        {
             if (cancellationToken.IsCancellationRequested) break;

            _logger.LogTrace("[StreamDebug] Received Raw Chunk ({ModelType}): {RawContent}", modelType, rawChunk.RawContent);

            var parsed = ParseRawChunk(rawChunk.RawContent, modelType);

            _logger.LogTrace("[StreamDebug] Parsed Chunk ({ModelType}): TextDelta='{TextDelta}', InputTokens={InputTokens}, OutputTokens={OutputTokens}",
                modelType, parsed.TextDelta ?? "NULL", parsed.InputTokens?.ToString() ?? "NULL", parsed.OutputTokens?.ToString() ?? "NULL");

            if(parsed.InputTokens.HasValue) latestReportedInputTokens = parsed.InputTokens.Value;
            if(parsed.OutputTokens.HasValue) latestReportedOutputTokens = parsed.OutputTokens.Value;
            if(rawChunk.IsCompletion && (parsed.InputTokens.HasValue || parsed.OutputTokens.HasValue)) finalTokensReported = true;

            // Determine chunk type and content based on parsed result
            string? textChunk = null;
            string? thinkingChunk = null;
            if (parsed.BlockType == "text" && !string.IsNullOrEmpty(parsed.TextDelta))
            {
                textChunk = parsed.TextDelta;
                currentBlockType = "text";
            }
            else if (parsed.BlockType == "thinking" && !string.IsNullOrEmpty(parsed.TextDelta))
            {
                thinkingChunk = parsed.TextDelta;
                currentBlockType = "thinking";
            }

            // --- Original Thinking/Answer Marker Logic (KEEPING FOR NOW as fallback/alternative) ---
            // This logic might become redundant if the Anthropic thinking parameter works well,
            // but let's keep it commented out for reference or potential future use.
            /*
            string effectiveChunk = parsed.TextDelta ?? string.Empty; // Use TextDelta directly here
 
            _logger.LogTrace("[StreamDebug] Extracted Delta Chunk: '{EffectiveChunk}'", effectiveChunk);
 
            if (string.IsNullOrEmpty(effectiveChunk) && !rawChunk.IsCompletion && string.IsNullOrEmpty(parsed.BlockType)) // Skip if no text/thinking
            {
                _logger.LogTrace("[StreamDebug] Skipping empty non-completion chunk.");
                continue;
            }
 
            if (supportsThinking)
            {
                int thinkingIndex = effectiveChunk.IndexOf(thinkingMarker);
                int answerIndex = effectiveChunk.IndexOf(answerMarker);
 
                if (thinkingIndex != -1 && !isInThinkingBlock)
                {
                    isInThinkingBlock = true;
                    string beforeMarker = effectiveChunk.Substring(0, thinkingIndex);
                    if (!string.IsNullOrWhiteSpace(beforeMarker)) accumulatedContent.Append(beforeMarker);
                    currentThinkingContent.Append(effectiveChunk.Substring(thinkingIndex + thinkingMarker.Length));
                }
                else if (answerIndex != -1 && isInThinkingBlock)
                { 
                    isInThinkingBlock = false;
                    string beforeMarker = effectiveChunk.Substring(0, answerIndex);
                    if (!string.IsNullOrWhiteSpace(beforeMarker)) currentThinkingContent.Append(beforeMarker);
                    
                    if (currentThinkingContent.Length > 0) {
                         await _mediator.Publish(new ThinkingChunkReceivedNotification(chatSessionId, aiMessage.Id, currentThinkingContent.ToString()), cancellationToken);
                         currentThinkingContent.Clear();
                    }
                    accumulatedContent.Append(effectiveChunk.Substring(answerIndex + answerMarker.Length));
                }
                else if (isInThinkingBlock)
                { 
                    currentThinkingContent.Append(effectiveChunk);
                }
                else
                { 
                    accumulatedContent.Append(effectiveChunk);
                }
            } 
            else
            {
                 accumulatedContent.Append(effectiveChunk);
            }
 
            _logger.LogTrace("[StreamDebug] Appending Text Chunk: '{TextChunk}' to Message {MessageId}", effectiveChunk, aiMessage.Id);
            aiMessage.AppendContent(effectiveChunk);
            
            if (!isInThinkingBlock && !string.IsNullOrEmpty(effectiveChunk))
            {
                 _logger.LogTrace("[StreamDebug] Publishing MessageChunkReceivedNotification with: '{TextChunk}'", effectiveChunk);
                 await _mediator.Publish(new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, effectiveChunk), cancellationToken);
            }
            */
            // --- End of Original Thinking/Answer Marker Logic ---

            // New logic based on Anthropic block types
            if (!string.IsNullOrEmpty(thinkingChunk))
            {
                _logger.LogTrace("[StreamDebug] Publishing ThinkingChunkReceivedNotification with: '{ThinkingChunk}'", thinkingChunk);
                await _mediator.Publish(new ThinkingChunkReceivedNotification(chatSessionId, aiMessage.Id, thinkingChunk), cancellationToken);
                // Do NOT append thinking chunk to the final message content
            }
            else if (!string.IsNullOrEmpty(textChunk))
            {
                 _logger.LogTrace("[StreamDebug] Appending Text Chunk: '{TextChunk}' to Message {MessageId}", textChunk, aiMessage.Id);
                 aiMessage.AppendContent(textChunk); // Only append actual text chunks

                 _logger.LogTrace("[StreamDebug] Publishing MessageChunkReceivedNotification with: '{TextChunk}'", textChunk);
                 await _mediator.Publish(new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, textChunk), cancellationToken);
            }
            // Optional: Log accumulated content periodically if needed
            // _logger.LogTrace("[StreamDebug] Accumulated Content Length: {Length}", accumulatedContent.Length);
        }
        
        // Log final accumulated state
        // Note: accumulatedContent StringBuilder is not used with the new block-based logic
        _logger.LogDebug("[StreamDebug] Finished stream processing for Message {MessageId}. Final Accumulated Content Length: {Length}. Final AI Message Content Length: {AiMsgLength}", 
            aiMessage.Id, accumulatedContent.Length, aiMessage.Content?.Length ?? 0);

        return (latestReportedInputTokens, latestReportedOutputTokens);
    }

    // Update record to include block type
    private record ParsedChunk(string? TextDelta, string? BlockType, int? InputTokens, int? OutputTokens);

    private ParsedChunk ParseRawChunk(string rawJson, ModelType modelType)
    {
        try
        {
            switch (modelType)
            {
                case ModelType.OpenAi:
                    return ParseOpenAiChunk(rawJson);
                case ModelType.Anthropic:
                    return ParseAnthropicChunk(rawJson);
                case ModelType.Gemini:
                    return ParseGeminiChunk(rawJson);
                case ModelType.DeepSeek:
                    return ParseDeepSeekChunk(rawJson);
                default:
                     _logger.LogWarning("Parsing not implemented for model type {ModelType}", modelType);
                     return new ParsedChunk(null, null, null, null);
            }
        }
        catch (JsonException jsonEx)
        { 
             _logger.LogError(jsonEx, "Failed to parse AI stream chunk for {ModelType}. RawChunk: {RawChunk}", modelType, rawJson);
              return new ParsedChunk(null, null, null, null);
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "Unexpected error parsing AI stream chunk for {ModelType}. RawChunk: {RawChunk}", modelType, rawJson);
              return new ParsedChunk(null, null, null, null);
        }
    }

    private ParsedChunk ParseOpenAiChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null;
        if (doc.RootElement.TryGetProperty("choices", out var choices) && 
            choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("delta", out var delta) && 
            delta.TryGetProperty("content", out var content) && 
            content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
        }
        return new ParsedChunk(text, "text", InputTokens: null, OutputTokens: null);
    }

    private ParsedChunk ParseAnthropicChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? blockType = null;

        if (doc.RootElement.TryGetProperty("type", out var typeElement))
        {
           string type = typeElement.GetString() ?? "";
           switch(type)
           { 
               case "content_block_delta":
                   if(doc.RootElement.TryGetProperty("delta", out var delta) && 
                      delta.TryGetProperty("text", out var textElement) && 
                      textElement.ValueKind == JsonValueKind.String)
                   { 
                        blockType = "text"; // Assume text if delta.text exists
                        text = textElement.GetString();
                   }
                   // Check specifically for thinking delta
                   else if (doc.RootElement.TryGetProperty("delta", out delta) &&
                            delta.TryGetProperty("type", out var deltaType) && deltaType.GetString() == "thinking_delta" &&
                            delta.TryGetProperty("thinking", out var thinkingElement) && thinkingElement.ValueKind == JsonValueKind.String)
                   {
                       blockType = "thinking";
                       text = thinkingElement.GetString(); // Use TextDelta field for thinking content too
                   }
                   break;
               case "message_start":
                   if (doc.RootElement.TryGetProperty("message", out var msg) && 
                       msg.TryGetProperty("usage", out var usage) &&
                       usage.TryGetProperty("input_tokens", out var iTok) && 
                       iTok.ValueKind == JsonValueKind.Number)
                   { 
                        inputTokens = iTok.GetInt32();
                   }
                   break;
                case "message_delta":
                    if (doc.RootElement.TryGetProperty("usage", out var deltaUsage) && 
                        deltaUsage.TryGetProperty("output_tokens", out var oTok) &&
                        oTok.ValueKind == JsonValueKind.Number)
                    { 
                        outputTokens = oTok.GetInt32();
                    }
                    break;
               // Other types like message_stop, ping, error are handled by the IsCompletion flag or ignored here
           }
        }
        return new ParsedChunk(text, blockType, inputTokens, outputTokens);
    }

    private ParsedChunk ParseGeminiChunk(string rawJson)
    {
         using var doc = JsonDocument.Parse(rawJson);
         string? text = null;
         int? inputTokens = null;
         int? outputTokens = null;

         try 
         { 
             if (doc.RootElement.TryGetProperty("candidates", out var candidates) && 
                 candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                 candidates[0].TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object &&
                 content.TryGetProperty("parts", out var parts) && 
                 parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 &&
                 parts[0].TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
             {
                 text = textElement.GetString();
             }
         } catch (Exception ex) {
            _logger?.LogError(ex, "Error extracting text content from Gemini chunk: {RawJson}", rawJson);
         }

          if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
          { 
                if(usage.TryGetProperty("promptTokenCount", out var pToken) && pToken.ValueKind == JsonValueKind.Number) 
                    inputTokens = pToken.GetInt32();
                if(usage.TryGetProperty("candidatesTokenCount", out var cToken) && cToken.ValueKind == JsonValueKind.Number) 
                    outputTokens = cToken.GetInt32();
                // Could also check totalTokenCount if needed
          }
          return new ParsedChunk(text, "text", inputTokens, outputTokens);
    }

    private ParsedChunk ParseDeepSeekChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null;
        string? blockType = "text"; // Default to text
        int? inputTokens = null;
        int? outputTokens = null;

        if (doc.RootElement.TryGetProperty("choices", out var choices) && 
            choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
           var firstChoice = choices[0];
           if(firstChoice.TryGetProperty("delta", out var delta) && 
              delta.TryGetProperty("content", out var content) && 
              content.ValueKind == JsonValueKind.String)
           {
               text = content.GetString();
               blockType = "text"; 
           }
           // Check for reasoning content within the delta
           else if (firstChoice.TryGetProperty("delta", out delta) && 
                    delta.TryGetProperty("reasoning_content", out var reasoningContentElement) && 
                    reasoningContentElement.ValueKind == JsonValueKind.String)
           {
               text = reasoningContentElement.GetString();
               blockType = "thinking";
           }
            
           if (firstChoice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind != JsonValueKind.Null &&
               doc.RootElement.TryGetProperty("usage", out var usage))
           {
                if(usage.TryGetProperty("prompt_tokens", out var pToken) && pToken.ValueKind == JsonValueKind.Number) 
                    inputTokens = pToken.GetInt32();
                if(usage.TryGetProperty("completion_tokens", out var cToken) && cToken.ValueKind == JsonValueKind.Number) 
                    outputTokens = cToken.GetInt32();
           }
        }
        // Handle potential top-level reasoning_content if not in delta (less common?)
        else if (doc.RootElement.TryGetProperty("reasoning_content", out var topLevelReasoning) && 
                 topLevelReasoning.ValueKind == JsonValueKind.String)
        {
            text = topLevelReasoning.GetString();
            blockType = "thinking";
        }
        return new ParsedChunk(text, blockType, inputTokens, outputTokens);
    }
     
    private async Task FinalizeTokenUsage(
        Guid chatSessionId, 
        AiModel aiModel, 
        int finalInputTokens, 
        int finalOutputTokens, 
        CancellationToken cancellationToken)
    {
         if (finalInputTokens > 0 || finalOutputTokens > 0)
         { 
               decimal finalCost = aiModel.CalculateCost(finalInputTokens, finalOutputTokens);
               _logger.LogInformation("Finalizing token usage for ChatSession {ChatSessionId}: Input={InputTokens}, Output={OutputTokens}, Cost={Cost}", 
                    chatSessionId, finalInputTokens, finalOutputTokens, finalCost);
               await _tokenUsageService.SetTokenUsageFromModelAsync(chatSessionId, finalInputTokens, finalOutputTokens, finalCost, cancellationToken);
         }
         else
         { 
             _logger.LogWarning("Final token counts not reported by provider for ChatSession {ChatSessionId}. Token usage not updated.", chatSessionId);
         }
    }
}