using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;

namespace Application.Features.Chats.GetChatById;

public sealed record GetChatByIdQuery(Guid ChatId) : IQuery<ChatDto>;