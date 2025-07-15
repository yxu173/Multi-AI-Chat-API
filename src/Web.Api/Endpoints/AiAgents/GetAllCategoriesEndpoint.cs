using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiAgents;

[Authorize]
public class GetAllCategoriesEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/aiagent/GetAllCategories");
        Description(x => x.Produces(200));
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var categories = Enum.GetNames(typeof(Domain.Enums.AgentCategories));
        return SendOkAsync(categories, ct);
    }
} 