using Application.Features.Admin.ProviderApiKeys.UpdateProviderApiKey;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;

namespace Web.Api.Endpoints.Admin.ProviderApiKeys;

[Authorize(Roles = "Admin")]
public class UpdateProviderApiKeyRequest
{
    public Guid Id { get; set; }
    public string ApiSecret { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int? MaxRequestsPerDay { get; set; }
    public bool IsActive { get; set; }
}

public class UpdateProviderApiKeyEndpoint : Endpoint<UpdateProviderApiKeyRequest>
{
    public override void Configure()
    {
        Put("/api/admin/provider-keys/{Id:guid}");
        Description(x => x.Produces(204).Produces(400).Produces(404));
    }

    public override async Task HandleAsync(UpdateProviderApiKeyRequest req, CancellationToken ct)
    {
        var command = new UpdateProviderApiKeyCommand(
            req.Id,
            req.ApiSecret,
            req.Label,
            req.MaxRequestsPerDay,
            req.IsActive
        );
        var result = await command.ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendNoContentAsync(ct);
        else
            await SendAsync(null, 400, ct);
    }
}