using Application.Features.Chats.BulkDeleteChats;
using Application.Features.Chats.CreateChatSession;
using Application.Features.Chats.DeleteChatById;
using Application.Features.Chats.ForkChat;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;
using Application.Features.Chats.GetChatBySeacrh;
using Application.Features.Chats.GetChatDetails;
using Application.Features.Chats.GetSharedChat;
using Application.Features.Chats.GetSortedChats;
using Application.Features.Chats.MoveChatToFolder;
using Application.Features.Chats.ShareChat;
using Application.Features.Chats.UpdateChatSession;
using Application.Services.Infrastructure;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Chats;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class ChatController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> CreateChatSession([Microsoft.AspNetCore.Mvc.FromBody] CreateChatSessionRequest request)
    {
        var result = await new CreateChatSessionCommand(
            UserId,
            request.ModelId,
            request.FolderId,
            request.AiAgentId,
            request.CustomApiKey,
            request.EnableThinking).ExecuteAsync();

        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id}")]
    public async Task<IResult> GetChatById([FromRoute] Guid id)
    {
        var result = await new GetChatByIdQuery(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll")]
    public async Task<IResult> GetAllChats()
    {
        var result = await new GetAllChatsByUserIdQuery(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id}")]
    public async Task<IResult> DeleteChat([FromRoute] Guid id)
    {
        var result = await new DeleteChatCommand(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id}/Details")]
    public async Task<IResult> GetChatDetails([FromRoute] Guid id)
    {
        var result = await new GetChatDetailsQuery(UserId, id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("Search")]
    public async Task<IResult> GetChatBySearch([Microsoft.AspNetCore.Mvc.FromQuery] string search)
    {
        var result = await new GetChatBySearchQuery(UserId, search).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("StopResponse/{messageId}")]
    public IResult StopResponse([FromRoute] Guid messageId,
        [FromServices] StreamingOperationManager streamingOperationManager)
    {
        bool stopped = streamingOperationManager.StopStreaming(messageId);
        if (stopped)
        {
            return Results.Ok(new { Message = "Streaming stopped successfully." });
        }
        else
        {
            return Results.NotFound(
                new { Message = "No active streaming operation found for the provided message ID." });
        }
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("ToggleThinking/{chatId}")]
    public async Task<IResult> ToggleThinking([FromRoute] Guid chatId,
        [Microsoft.AspNetCore.Mvc.FromBody] ToggleThinkingRequest request,
        [FromServices] IChatSessionRepository chatSessionRepository,
        [FromServices] IAiModelRepository aiModelRepository)
    {
        try
        {
            var session = await chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(chatId);

            if (session.UserId != UserId)
            {
                return Results.Forbid();
            }

            var aiModel = await aiModelRepository.GetByIdAsync(session.AiModelId);
            if (request.Enable && (aiModel == null || !aiModel.SupportsThinking))
            {
                return Results.BadRequest(new
                {
                    Message = "This AI model does not support thinking mode",
                    AiModelName = aiModel?.Name ?? "Unknown model"
                });
            }

            session.ToggleThinking(request.Enable);

            await new UpdateChatSessionCommand(chatId, session.Title, session.FolderId).ExecuteAsync();

            return Results.Ok(new { Enabled = request.Enable });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("Update/{id}")]
    public async Task<IResult> UpdateChatSession([FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpdateChatSessionRequest request)
    {
        var result = await new UpdateChatSessionCommand(id, request.Title).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("Update/{id}/MoveToFolder")]
    public async Task<IResult> MoveChatToFolder([FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] MoveChatToFolderRequest request)
    {
        var result = await new MoveChatToFolderCommand(id, request.FolderId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll/OldestFirst")]
    public async Task<IResult> GetOldestChatsFirst()
    {
        var result = await new GetSortedChatsQuery(UserId, ChatSortOrder.OldestFirst).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll/TitleAZ")]
    public async Task<IResult> GetChatsTitleAZ()
    {
        var result = await new GetSortedChatsQuery(UserId, ChatSortOrder.TitleAZ).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll/TitleZA")]
    public async Task<IResult> GetChatsTitleZA()
    {
        var result = await new GetSortedChatsQuery(UserId, ChatSortOrder.TitleZA).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("BulkDelete")]
    public async Task<IResult> BulkDeleteChats([Microsoft.AspNetCore.Mvc.FromBody] BulkDeleteChatsRequest request)
    {
        var result = await new BulkDeleteChatsCommand(UserId, request.ChatIds).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("{id}/Share")]
    public async Task<IResult> ShareChat([FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] ShareChatRequest request)
    {
        var result = await new ShareChatCommand(id, UserId, request.ExpiresAt).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("Shared/{token}")]
    [AllowAnonymous]
    public async Task<IResult> GetSharedChat([FromRoute] string token)
    {
        var result = await new GetSharedChatQuery(token).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("Fork")]
    public async Task<IResult> ForkChat([Microsoft.AspNetCore.Mvc.FromBody] ForkChatRequest request)
    {
        var result =
            await new ForkChatCommand(UserId, request.OriginalChatId, request.ForkFromMessageId, request.NewAiModelId)
                .ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}