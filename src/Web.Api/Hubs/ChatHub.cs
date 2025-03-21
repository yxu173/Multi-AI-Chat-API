using Microsoft.AspNetCore.SignalR;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly StreamingOperationManager _streamingOperationManager;

    public ChatHub(ChatService chatService, StreamingOperationManager streamingOperationManager)
    {
        _chatService = chatService;
        _streamingOperationManager = streamingOperationManager;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChatSession(string chatSessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
    }

    public async Task SendMessage(string chatSessionId, string content)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content);
    }

    public async Task EditLastMessage(string chatSessionId, string newContent)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        await _chatService.EditLastUserMessageAsync(Guid.Parse(chatSessionId), userId, newContent);
    }

    public async Task StopResponse(string messageId, string chatSessionId)
    {
        _streamingOperationManager.StopStreaming(Guid.Parse(messageId));
        await Clients.Group(chatSessionId)
            .SendAsync("ResponseStopped", messageId);
    }
}