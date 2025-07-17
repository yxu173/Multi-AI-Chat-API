using FastEndpoints;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Application.Features.Chats.GetChatById;

namespace Web.Api.Endpoints.Chats;

public class ChatSseEndpoint : EndpointWithoutRequest
{
    private static readonly ConcurrentQueue<ChatDto> _chatEvents = new();

    public override void Configure()
    {
        Get("/api/chat/stream");
        AllowAnonymous();
        Options(x => x.RequireCors(p => p.AllowAnyOrigin()));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendEventStreamAsync("chat-update", GetChatStream(ct), ct);
    }

    private async IAsyncEnumerable<ChatDto> GetChatStream([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            while (_chatEvents.TryDequeue(out var chatEvent))
            {
                yield return chatEvent;
            }

            await Task.Delay(1000, ct);
        }
    }

    public static void NotifyNewChat(ChatDto chatDto)
    {
        _chatEvents.Enqueue(chatDto);
    }
}