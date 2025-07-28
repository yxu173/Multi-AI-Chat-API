using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Logging;

namespace Application.Services.Utilities;

public class ChatTitleGenerator
{
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly ILogger<ChatTitleGenerator> _logger;
    private const int MaxTitleLength = 50;

    public ChatTitleGenerator(
        IAiModelServiceFactory aiModelServiceFactory,
        ILogger<ChatTitleGenerator> logger)
    {
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GenerateTitleAsync(ChatSession chatSession, List<Message> messages, CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceContext = await _aiModelServiceFactory.GetServiceContextAsync(
                chatSession.UserId,
               Guid.Parse("617453dd-c7e5-4f57-bb79-02d2d8ac38dc"),
                null,
                cancellationToken);

            if (serviceContext == null)
            {
                _logger.LogWarning("Could not get Gemini service context for title generation. Falling back to simple title.");
                return GenerateSimpleTitle(messages[0].Content);
            }

            var prompt = $"Generate only a title (max {MaxTitleLength} chars) for a chat conversation based on this first message: {messages[0].Content} and this AI response : {messages[1].Content}. The title should be clear and informative.";
            
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
                        temperature = 0.7,
                        maxOutputTokens = 25
                    }
                }
            );

            var title = "";
            await foreach (var chunk in serviceContext.Service.StreamResponseAsync(requestPayload, cancellationToken))
            {
                if (chunk.TextDelta != null)
                {
                    title += chunk.TextDelta;
                }
            }

            title = title.Trim().Trim('"', '\'', '`');
            if (title.Length > MaxTitleLength)
            {
                title = title.Substring(0, MaxTitleLength - 3) + "...";
            }

            return string.IsNullOrWhiteSpace(title) ? GenerateSimpleTitle(messages[0].Content) : title;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate title with Gemini. Falling back to simple title.");
            return GenerateSimpleTitle(messages[0].Content);
        }
    }

    private static string GenerateSimpleTitle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "New Chat";
        return content.Length <= MaxTitleLength ? content : content.Substring(0, MaxTitleLength) + "...";
    }
} 