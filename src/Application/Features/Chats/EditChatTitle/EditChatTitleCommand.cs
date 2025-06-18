using Application.Abstractions.Messaging;

namespace Application.Features.Chats.EditChatTitle;

public sealed record EditChatTitleCommand(Guid ChatId, string Title) : ICommand<bool>;