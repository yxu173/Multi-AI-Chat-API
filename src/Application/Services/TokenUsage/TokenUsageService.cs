using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

namespace Application.Services.TokenUsage;

public class TokenUsageService
{
    private readonly IChatTokenUsageRepository _tokenUsageRepository;
    private readonly IMediator _mediator;

    public TokenUsageService(IChatTokenUsageRepository tokenUsageRepository, IMediator mediator)
    {
        _tokenUsageRepository = tokenUsageRepository ?? throw new ArgumentNullException(nameof(tokenUsageRepository));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task<ChatTokenUsage> GetOrCreateTokenUsageAsync(Guid chatSessionId,
        CancellationToken cancellationToken = default)
    {
        var tokenUsage = await _tokenUsageRepository.GetByChatSessionIdAsync(chatSessionId) ??
                         await _tokenUsageRepository.AddAsync(ChatTokenUsage.Create(chatSessionId, 0, 0, 0),
                             cancellationToken);
        return tokenUsage;
    }

    public async Task UpdateTokenUsageAsync(Guid chatSessionId, int inputTokens, int outputTokens, decimal cost,
        CancellationToken cancellationToken = default)
    {
        var tokenUsage = await GetOrCreateTokenUsageAsync(chatSessionId, cancellationToken);
        
        // Pass the incremental values directly - the entity will add them to existing totals
        tokenUsage.UpdateTokenCountsAndCost(inputTokens, outputTokens, cost);
        await _tokenUsageRepository.UpdateAsync(tokenUsage, cancellationToken);
        await _mediator.Publish(
            new TokenUsageUpdatedNotification(chatSessionId, tokenUsage.InputTokens, tokenUsage.OutputTokens,
                tokenUsage.TotalCost), cancellationToken);
    }
    
    public async Task SetTokenUsageFromModelAsync(Guid chatSessionId, int totalInputTokens, int totalOutputTokens, 
        decimal modelCost, CancellationToken cancellationToken = default)
    {
        var tokenUsage = await GetOrCreateTokenUsageAsync(chatSessionId, cancellationToken);
        
        // Create a new update method for direct setting values
        tokenUsage.SetTokenCountsAndCost(totalInputTokens, totalOutputTokens, modelCost);
        await _tokenUsageRepository.UpdateAsync(tokenUsage, cancellationToken);
        await _mediator.Publish(
            new TokenUsageUpdatedNotification(chatSessionId, tokenUsage.InputTokens, tokenUsage.OutputTokens,
                tokenUsage.TotalCost), cancellationToken);
    }
}