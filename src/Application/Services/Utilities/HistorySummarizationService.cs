using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Application.Services.Utilities;

public class HistorySummarizationService
{
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly ILogger<HistorySummarizationService> _logger;
    private readonly Guid _geminiModelId = Guid.Parse("c581c22c-5466-42d7-abe1-ce8a6ff62ce6");
    private const int SummaryMaxTokenLength = 500;

    public HistorySummarizationService(
        IAiModelServiceFactory aiModelServiceFactory,
        ILogger<HistorySummarizationService> logger)
    {
        _aiModelServiceFactory = aiModelServiceFactory;
        _logger = logger;
    }

    public async Task<string?> GenerateSummaryAsync(ChatSession chatSession, CancellationToken cancellationToken)
    {
        try
        {
            var serviceContext = await _aiModelServiceFactory.GetServiceContextAsync(
                chatSession.UserId,
                _geminiModelId,
                null,
                cancellationToken);

            if (serviceContext == null)
            {
                _logger.LogWarning("Could not get Gemini service context for history summarization for chat {ChatSessionId}.", chatSession.Id);
                return null;
            }

            var conversationHistoryText = new StringBuilder();
            foreach (var message in chatSession.Messages)
            {
                var prefix = message.IsFromAi ? "AI: " : "User: ";
                conversationHistoryText.AppendLine($"{prefix}{message.Content}");
            }

            var prompt = $"Concisely summarize the following conversation. The summary should capture the key points and the main progression of the dialogue. The summary will be used as a system prompt for an AI to continue the conversation later, so it must be accurate and provide enough context.\n\nConversation:\n{conversationHistoryText.ToString()}";
            
            var requestPayload = new AiRequestPayload(
                new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.5,
                        maxOutputTokens = SummaryMaxTokenLength
                    }
                }
            );
            
            var summaryBuilder = new StringBuilder();
            await foreach (var chunk in serviceContext.Service.StreamResponseAsync(requestPayload, cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    summaryBuilder.Append(chunk.TextDelta);
                }
            }

            var summary = summaryBuilder.ToString().Trim();

            if (string.IsNullOrWhiteSpace(summary))
            {
                _logger.LogWarning("Gemini returned an empty or whitespace summary for chat {ChatSessionId}.", chatSession.Id);
                return null;
            }
            
            _logger.LogInformation("Successfully generated history summary for chat {ChatSessionId}.", chatSession.Id);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate history summary with Gemini for chat {ChatSessionId}.", chatSession.Id);
            return null;
        }
    }
} 