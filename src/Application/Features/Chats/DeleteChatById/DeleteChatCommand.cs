using Application.Abstractions.Messaging;

namespace Application.Features.Chats.DeleteChatById;

public sealed record DeleteChatCommand(Guid Id) : ICommand<bool>;