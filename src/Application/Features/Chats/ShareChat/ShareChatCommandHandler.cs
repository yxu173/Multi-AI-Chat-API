using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.ShareChat;

public class ShareChatCommandHandler : ICommandHandler<ShareChatCommand, string>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly ISharedChatRepository _sharedChatRepository;

    public ShareChatCommandHandler(
        IChatSessionRepository chatSessionRepository,
        ISharedChatRepository sharedChatRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _sharedChatRepository = sharedChatRepository;
    }

    public async Task<Result<string>> ExecuteAsync(ShareChatCommand command, CancellationToken ct)
    {
        var chat = await _chatSessionRepository.GetByIdAsync(command.ChatId);
        if (chat == null)
        {
            return Result.Failure<string>(Error.NotFound("Chat.NotFound", "Chat not found"));
        }

        if (chat.UserId != command.OwnerId)
        {
            return Result.Failure<string>(Error.Conflict("Chat.NotOwner", "You are not the owner of this chat"));
        }

        var existingShare = await _sharedChatRepository.GetByChatIdAsync(command.ChatId, ct);
        if (existingShare != null)
        {
            return Result.Success(existingShare.ShareToken);
        }

        var sharedChat = SharedChat.Create(
            command.ChatId,
            command.OwnerId,
            command.ExpiresAt);

        await _sharedChatRepository.AddAsync(sharedChat, ct);
        return Result.Success(sharedChat.ShareToken);
    }
} 