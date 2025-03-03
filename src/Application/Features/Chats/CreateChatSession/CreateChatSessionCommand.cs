using Application.Abstractions.Messaging;
using Domain.Enums;

namespace Application.Features.Chats.CreateChatSession;

public record CreateChatSessionCommand(Guid UserId, Guid ModelId, string? customApiKey = null) : ICommand<Guid>;