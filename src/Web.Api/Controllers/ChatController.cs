using Application.Features.Chats.CreateChatSession;
using Application.Features.Chats.DeleteChatById;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;
using Application.Features.Chats.GetChatBySeacrh;
using Application.Features.Chats.GetChatDetails;
using Application.Features.Chats.UpdateChatSession;
using Application.Services.Chat;
using Application.Services.Infrastructure;
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
        [FromServices] ChatSessionService chatSessionService,
        [FromServices] Domain.Repositories.IAiModelRepository aiModelRepository)
    {
        try
        {
            var session = await chatSessionService.GetChatSessionAsync(chatId);

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
}