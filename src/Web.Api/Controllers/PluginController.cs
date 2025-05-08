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
        var result = await new CreatePluginCommand(request.Name, request.Description, request.IconUrl).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("AddUserPlugin")]
    public async Task<IResult> AddUserPlugin([Microsoft.AspNetCore.Mvc.FromBody] AddUserPluginRequest request)
    {
        var result = await new AddUserPluginCommand(UserId, request.PluginId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("AddChatPlugin")]
    public async Task<IResult> AddChatPlugin([Microsoft.AspNetCore.Mvc.FromBody] AddChatPluginRequest request)
    {
        var result = await new AddPluginToChatCommand(request.ChatId, request.PluginId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet]
    public async Task<IResult> GetAllPlugins()
    {
        var result = await new GetAllPluginsQuery().ExecuteAsync();
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