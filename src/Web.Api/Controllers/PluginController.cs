using Application.Features.Plugins.AddPluginToChat;
using Application.Features.Plugins.AddUserPlugin;
using Application.Features.Plugins.CreatePlugin;
using Application.Features.Plugins.DeleteChatSessionPlugin;
using Application.Features.Plugins.GetAllPlugins;
using FastEndpoints;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Plugins;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class PluginController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("create")]
    public async Task<IResult> Create([Microsoft.AspNetCore.Mvc.FromBody] CreatePluginRequest request)
    {
        var command = new CreatePluginCommand(request.Name, request.Description, request.IconUrl);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("AddUserPlugin")]
    public async Task<IResult> AddUserPlugin([Microsoft.AspNetCore.Mvc.FromBody] AddUserPluginRequest request)
    {
        var command = new AddUserPluginCommand(UserId, request.PluginId);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("AddChatPlugin")]
    public async Task<IResult> AddChatPlugin([Microsoft.AspNetCore.Mvc.FromBody] AddChatPluginRequest request)
    {
        var command = new AddPluginToChatCommand(request.ChatId, request.PluginId);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet]
    public async Task<IResult> GetAllPlugins()
    {
        var query = new GetAllPluginsQuery();
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id}/DeletePlugin")]
    public async Task<IResult> DeletePluginFromChat([FromRoute] Guid id)
    {
        var result = await new DeleteChatSessionPluginCommand(id)
            .ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}