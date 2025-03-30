using Application.Features.Chats.CreateChatSession;
using Application.Features.Chats.DeleteChatById;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;
using Application.Features.Chats.GetChatBySeacrh;
using Application.Features.Chats.UpdateChatSession;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Chats;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class ChatController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreateChatSession([FromBody] CreateChatSessionRequest request)
    {
        var command = new CreateChatSessionCommand(
            UserId, 
            request.ModelId, 
            request.FolderId, 
            request.AiAgentId, 
            request.CustomApiKey, 
            request.EnableThinking);
            
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("{id}")]
    public async Task<IResult> GetChatById([FromRoute] Guid id)
    {
        var query = new GetChatByIdQuery(id);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetAll")]
    public async Task<IResult> GetAllChats()
    {
        var query = new GetAllChatsByUserIdQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpDelete("{id}")]
    public async Task<IResult> DeleteChat([FromRoute] Guid id)
    {
        var command = new DeleteChatCommand(id);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("Search")]
    public async Task<IResult> GetChatBySearch([FromQuery] string search)
    {
        var query = new GetChatBySearchQuery(UserId, search);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPost("StopResponse/{messageId}")]
    public IResult StopResponse([FromRoute] Guid messageId, [FromServices] Application.Services.StreamingOperationManager streamingOperationManager)
    {
        bool stopped = streamingOperationManager.StopStreaming(messageId);
        if (stopped)
        {
            return Results.Ok(new { Message = "Streaming stopped successfully." });
        }
        else
        {
            return Results.NotFound(new { Message = "No active streaming operation found for the provided message ID." });
        }
    }

    [HttpPost("ToggleThinking/{chatId}")]
    public async Task<IResult> ToggleThinking([FromRoute] Guid chatId, [FromBody] ToggleThinkingRequest request, 
        [FromServices] Application.Services.ChatSessionService chatSessionService,
        [FromServices] Domain.Repositories.IAiModelRepository aiModelRepository)
    {
        try
        {
            var session = await chatSessionService.GetChatSessionAsync(chatId);
            
            // Check if the chat belongs to the current user
            if (session.UserId != UserId)
            {
                return Results.Forbid();
            }
            
            // Check if AI model supports thinking mode
            var aiModel = await aiModelRepository.GetByIdAsync(session.AiModelId);
            if (request.Enable && (aiModel == null || !aiModel.SupportsThinking))
            {
                return Results.BadRequest(new 
                { 
                    Message = "This AI model does not support thinking mode",
                    AiModelName = aiModel?.Name ?? "Unknown model"
                });
            }
            
            // Toggle thinking mode
            session.ToggleThinking(request.Enable);
            
            await _mediator.Send(new UpdateChatSessionCommand(chatId, session.Title, session.FolderId));
            
            return Results.Ok(new { Enabled = request.Enable });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}