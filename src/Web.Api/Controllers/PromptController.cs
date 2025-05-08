using Application.Features.Prompts.CreatePrompt;
using Application.Features.Prompts.DeletePrompt;
using Application.Features.Prompts.GetAllPromptsByUserId;
using Application.Features.Prompts.UpdatePrompt;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Prompts;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class PromptController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> CreatePrompt([Microsoft.AspNetCore.Mvc.FromBody] CreatePromptRequest request)
    {
        var result =
            await new CreatePromptCommand(UserId, request.Title, request.Description, request.Content, request.Tags)
                .ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetMyPrompts")]
    public async Task<IResult> GetMyPrompts()
    {
        var result = await new GetAllPromptsByUserIdQuery(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id}/GetAllPrompts")]
    public async Task<IResult> GetAllPrompts([FromRoute] Guid id)
    {
        var result = await new GetAllPromptsByUserIdQuery(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id}")]
    public async Task<IResult> DeletePrompt([FromRoute] Guid id)
    {
        var result = await new DeletePromptCommand(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("{id}")]
    public async Task<IResult> UpdatePrompt([FromRoute] Guid id, CreatePromptRequest request)
    {
        var result = await new UpdatePromptCommand(id, UserId, request.Title, request.Description, request.Content,
            request.Tags).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}