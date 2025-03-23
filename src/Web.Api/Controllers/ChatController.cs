using Application.Features.Chats.CreateChatSession;
using Application.Features.Chats.DeleteChatById;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;
using Application.Features.Chats.GetChatBySeacrh;
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
        var command = new CreateChatSessionCommand(UserId, request.ModelId, request.FolderId);
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
}