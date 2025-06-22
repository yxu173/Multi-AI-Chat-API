using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using SharedKernal;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace Application.Features.Chats.ForkChat;

public class ForkChatHandler : ICommandHandler<ForkChatCommand, ForkChatResponse>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAiModelRepository _aiModelRepository;
    private readonly ILogger<ForkChatHandler> _logger;

    public ForkChatHandler(
        IChatSessionRepository chatSessionRepository,
        IAiModelRepository aiModelRepository,
        ILogger<ForkChatHandler> logger)
    {
        _chatSessionRepository = chatSessionRepository;
        _aiModelRepository = aiModelRepository;
        _logger = logger;
    }

    public async Task<Result<ForkChatResponse>> ExecuteAsync(ForkChatCommand request, CancellationToken cancellationToken)
    {
        var originalChat = await _chatSessionRepository.GetByIdAsync(request.OriginalChatSessionId);
        if (originalChat is null || originalChat.UserId != request.UserId)
        {
            return Result.Failure<ForkChatResponse>(Error.NotFound("ForkChat.NotFound", "Original chat session not found or access denied."));
        }

        var forkFromMessage = originalChat.Messages.FirstOrDefault(m => m.Id == request.ForkFromMessageId);
        if (forkFromMessage is null)
        {
            return Result.Failure<ForkChatResponse>(Error.NotFound("ForkChat.MessageNotFound", "Message to fork from not found in the specified chat."));
        }

        var newAiModel = await _aiModelRepository.GetByIdAsync(request.NewAiModelId);
        if (newAiModel is null)
        {
            return Result.Failure<ForkChatResponse>(Error.NotFound("ForkChat.ModelNotFound", "The selected AI model for the new chat was not found."));
        }

        var forkedChat = Domain.Aggregates.Chats.ChatSession.Create(request.UserId, request.NewAiModelId, originalChat.FolderId, originalChat.AiAgentId);
        var newChatTitle = $"[Forked] {originalChat.Title}";
        forkedChat.UpdateTitle(newChatTitle);

        var messagesToCopy = originalChat.Messages
            .Where(m => m.CreatedAt <= forkFromMessage.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var originalMessage in messagesToCopy)
        {
            var newMessage = originalMessage.CloneForNewChat(forkedChat.Id);
            forkedChat.AddMessage(newMessage);
        }

        await _chatSessionRepository.AddAsync(forkedChat, cancellationToken);

        _logger.LogInformation("Chat {OriginalChatId} forked to new chat {NewChatId} by user {UserId}",
            request.OriginalChatSessionId, forkedChat.Id, request.UserId);

        return Result.Success(new ForkChatResponse(forkedChat.Id));
    }
} 