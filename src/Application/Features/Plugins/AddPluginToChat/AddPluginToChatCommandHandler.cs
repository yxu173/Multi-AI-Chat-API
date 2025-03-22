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

    public async Task<Result<Guid>> Handle(AddPluginToChatCommand request, CancellationToken cancellationToken)
    {
        var chatSessionPlugin = ChatSessionPlugin.Create(request.ChatId, request.PluginId, request.Order);
        await _chatSessionPluginRepository.AddAsync(chatSessionPlugin, cancellationToken);
        return Result.Success(chatSessionPlugin.Id);
    }
}