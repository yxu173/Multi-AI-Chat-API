using Application.Abstractions.Messaging;
using Application.Features.Chats.GetChatById;

namespace Application.Features.Chats.GetSharedChat;

public sealed record GetSharedChatQuery(string ShareToken) : IQuery<ChatDto>; 