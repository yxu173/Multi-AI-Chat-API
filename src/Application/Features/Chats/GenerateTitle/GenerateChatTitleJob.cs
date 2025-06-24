using Application.Services.Utilities;
using Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Features.Chats.GenerateTitle;

public class GenerateChatTitleJob
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly ChatTitleGenerator _titleGenerator;
    private readonly ILogger<GenerateChatTitleJob> _logger;

    public GenerateChatTitleJob(
        IChatSessionRepository chatSessionRepository,
        ChatTitleGenerator titleGenerator,
        ILogger<GenerateChatTitleJob> logger)
    {
        _chatSessionRepository = chatSessionRepository;
        _titleGenerator = titleGenerator;
        _logger = logger;
    }

    [AutomaticRetry(OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task GenerateAsync(Guid chatSessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting title generation job for chat {ChatSessionId}", chatSessionId);

        var chatSession = await _chatSessionRepository.GetByIdAsync(chatSessionId);

        if (chatSession.Messages.Count < 2)
        {
            _logger.LogInformation("Not enough messages to generate a title for chat {ChatSessionId}. Needs at least 2.", chatSessionId);
            return;
        }

        var title = await _titleGenerator.GenerateTitleAsync(chatSession, chatSession.Messages.ToList(), cancellationToken);

        if (!string.IsNullOrWhiteSpace(title) && title != chatSession.Title)
        {
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession, cancellationToken);
            _logger.LogInformation("Successfully updated chat {ChatSessionId} with a new title: {Title}", chatSessionId, title);
        }
        else
        {
            _logger.LogWarning("Title generation for chat {ChatSessionId} did not produce a new result.", chatSessionId);
        }
    }
} 