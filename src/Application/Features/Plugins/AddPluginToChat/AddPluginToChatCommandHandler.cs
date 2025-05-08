using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Plugins.AddPluginToChat;

public sealed class AddPluginToChatCommandHandler : ICommandHandler<AddPluginToChatCommand, Guid>
{
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository;

    public AddPluginToChatCommandHandler(IChatSessionPluginRepository chatSessionPluginRepository)
    {
        _chatSessionPluginRepository = chatSessionPluginRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(AddPluginToChatCommand request, CancellationToken ct)
    {
        var chatSessionPlugin = ChatSessionPlugin.Create(request.ChatId, request.PluginId);
        await _chatSessionPluginRepository.AddAsync(chatSessionPlugin, ct);
        return Result.Success(chatSessionPlugin.Id);
    }

}