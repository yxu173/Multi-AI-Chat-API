using Application.Features.Plugins.AddPluginToChat;
using Application.Features.Plugins.AddUserPlugin;
using Application.Features.Plugins.CreatePlugin;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Plugins;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class PluginController : BaseController
{
    [HttpPost("create")]
    public async Task<IResult> Create([FromBody] CreatePluginRequest request)
    {
        var command = new CreatePluginCommand(request.Name, request.Description, request.PluginType,
            request.ParametersSchema);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPost("AddUserPlugin")]
    public async Task<IResult> AddUserPlugin([FromBody] AddUserPluginRequest request)
    {
        var command = new AddUserPluginCommand(UserId, request.PluginId);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPost("AddChatPlugin")]
    public async Task<IResult> AddChatPlugin([FromBody] AddChatPluginRequest request)
    {
        var command = new AddPluginToChatCommand(request.ChatId, request.PluginId, request.Order);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}