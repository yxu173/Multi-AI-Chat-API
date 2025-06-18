using Application.Features.ChatFolders.CreateChatFolder;
using Application.Features.ChatFolders.DeleteChatFolder;
using Application.Features.ChatFolders.GetChatFolderById;
using Application.Features.ChatFolders.GetChatsFolderByUserId;
using Application.Features.ChatFolders.UpdateChatFolder;
using FastEndpoints;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.ChatFolders;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class ChatFolderController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> CreateFolder([Microsoft.AspNetCore.Mvc.FromBody] CreateFolderRequest request)
    {
        var result =
            await CommandExtensions.ExecuteAsync(new CreateChatFolderCommand(UserId, request.Name));
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("update/{id}")]
    public async Task<IResult> UpdateFolder([FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpdateFolderRequest request)
    {
        var result = await new UpdateChatFolderCommand(id, request.Name).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id}")]
    public async Task<IResult> DeleteFolder([FromRoute] Guid id)
    {
        var result = await new DeleteChatFolderCommand(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id}")]
    public async Task<IResult> GetChatFolderById([FromRoute] Guid id)
    {
        var result = await new GetChatFolderByIdQuery(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll")]
    public async Task<IResult> GetAllChatFolders()
    {
        var result = await new GetChatsFolderByUserIdQuery(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}