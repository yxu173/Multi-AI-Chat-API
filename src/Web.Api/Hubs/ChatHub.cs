using Microsoft.AspNetCore.SignalR;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Application.Notifications;
using System.Threading;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IMediator _mediator;

    public ChatHub(
        ChatService chatService, 
        StreamingOperationManager streamingOperationManager,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IMediator mediator)
    {
        _chatService = chatService;
        _streamingOperationManager = streamingOperationManager;
        _messageRepository = messageRepository;
        _fileAttachmentRepository = fileAttachmentRepository;
        _mediator = mediator;
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

    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId), newContent);
    }

    public async Task NotifyFileUploaded(string chatSessionId, string messageId, string fileId)
    {
        if (!Guid.TryParse(fileId, out var fileGuid) || 
            !Guid.TryParse(messageId, out var messageGuid) || 
            !Guid.TryParse(chatSessionId, out var chatSessionGuid))
        {
            return;
        }

        var userId = Guid.Parse(Context.UserIdentifier);
        
        // Get the file attachment
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileGuid, CancellationToken.None);
        if (fileAttachment == null)
        {
            return;
        }
        
        // Get the message
        var message = await _messageRepository.GetByIdAsync(messageGuid, CancellationToken.None);
        if (message == null || message.UserId != userId)
        {
            return;
        }
        
        // Notify other clients in the chat group
        await _mediator.Publish(new FileUploadedNotification(chatSessionGuid, fileAttachment));
    }
}