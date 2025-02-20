using Application.Features.Chats.CreateChatSession;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Chats;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;
[Authorize]
public class ChatController : BaseController
{
    [HttpPost("CreateChatSession")]
    public async Task<IResult> CreateChatSession([FromBody] CreateChatSessionRequest request)
    {
        var command = new CreateChatSessionCommand(UserId,request.ModelType);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}