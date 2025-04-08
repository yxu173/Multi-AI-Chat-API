using Microsoft.Extensions.Logging;
using Domain.Aggregates.Chats;

namespace Application.Services.Streaming;

public class TokenUsageTracker
{
    private readonly ILogger<TokenUsageTracker> _logger;
    private readonly TokenUsageService _tokenUsageService;

    public TokenUsageTracker(
        ILogger<TokenUsageTracker> logger,
        TokenUsageService tokenUsageService)
    {
        _logger = logger;
        _tokenUsageService = tokenUsageService;
    }

    public async Task FinalizeTokenUsage(
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