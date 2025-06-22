using Domain.Repositories;
using Application.Services.Utilities;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace Application.Features.Chats.SummarizeHistory;

public class SummarizeChatHistoryJob
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly HistorySummarizationService _summarizationService;
    private readonly ILogger<SummarizeChatHistoryJob> _logger;

    public SummarizeChatHistoryJob(
        IChatSessionRepository chatSessionRepository,
        HistorySummarizationService summarizationService,
        ILogger<SummarizeChatHistoryJob> logger)
    {
        _chatSessionRepository = chatSessionRepository;
        _summarizationService = summarizationService;
        _logger = logger;
    }

    public async Task SummarizeAsync(Guid chatSessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting history summarization job for chat {ChatSessionId}", chatSessionId);

        var chatSession = await _chatSessionRepository.GetByIdAsync(chatSessionId);

        if (chatSession is null)
        {
            _logger.LogWarning("Chat session {ChatSessionId} not found for summarization.", chatSessionId);
            return;
        }

        if (chatSession.LastSummarizedAt.HasValue &&
            chatSession.Messages.All(m => m.CreatedAt <= chatSession.LastSummarizedAt.Value))
        {
            _logger.LogInformation("No new messages in chat {ChatSessionId} since last summarization. Skipping job.", chatSessionId);
            return;
        }

        var summary = await _summarizationService.GenerateSummaryAsync(chatSession, cancellationToken);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            chatSession.UpdateHistorySummary(summary);
            await _chatSessionRepository.UpdateAsync(chatSession, cancellationToken);
            _logger.LogInformation("Successfully updated chat {ChatSessionId} with a new history summary.", chatSessionId);
        }
        else
        {
            _logger.LogWarning("History summarization for chat {ChatSessionId} did not produce a result.", chatSessionId);
        }
    }
} 