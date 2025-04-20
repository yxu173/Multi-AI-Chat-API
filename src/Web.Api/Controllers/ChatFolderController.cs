using Application.Features.ChatFolders.CreateChatFolder;
using Application.Features.ChatFolders.DeleteChatFolder;
using Application.Features.ChatFolders.GetChatFolderById;
using Application.Features.ChatFolders.GetChatsFolderByUserId;
using Application.Features.ChatFolders.UpdateChatFolder;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.ChatFolders;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class ChatFolderController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        var command = new CreateChatFolderCommand(UserId, request.Name, request.Description);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPut("update/{id}")]
    public async Task<IResult> UpdateFolder([FromRoute] Guid id, [FromBody] UpdateFolderRequest request)
    {
        var command = new UpdateChatFolderCommand(id, request.Name, request.Description);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpDelete("{id}")]
    public async Task<IResult> DeleteFolder([FromRoute] Guid id)
    {
        var command = new DeleteChatFolderCommand(id);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("{id}")]
    public async Task<IResult> GetChatFolderById([FromRoute] Guid id)
    {
        var query = new GetChatFolderByIdQuery(id);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetAll")]
    public async Task<IResult> GetAllChatFolders()
    {
        var query = new GetChatsFolderByUserIdQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}