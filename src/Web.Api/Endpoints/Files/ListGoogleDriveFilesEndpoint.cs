using Application.Services.Files;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Web.Api.Endpoints.Files;

public class ListGoogleDriveFilesResponse
{
    public List<GoogleDriveFileDto> Files { get; set; } = new();
}

public class GoogleDriveFileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string? IconLink { get; set; }
    public string? WebViewLink { get; set; }
}

[Authorize]
public class ListGoogleDriveFilesEndpoint : EndpointWithoutRequest<ListGoogleDriveFilesResponse>
{
    private readonly GoogleDriveService _googleDriveService;
    private readonly UserManager<Domain.Aggregates.Users.User> _userManager;

    public ListGoogleDriveFilesEndpoint(GoogleDriveService googleDriveService, UserManager<Domain.Aggregates.Users.User> userManager)
    {
        _googleDriveService = googleDriveService;
        _userManager = userManager;
    }

    public override void Configure()
    {
        Get("/api/file/list-google-drive-files");
        Description(x => x.Produces(200, typeof(ListGoogleDriveFilesResponse)).Produces(401));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var files = await _googleDriveService.ListFilesAsync(user, ct);
        var response = new ListGoogleDriveFilesResponse
        {
            Files = files.Select(f => new GoogleDriveFileDto
            {
                Id = f.Id ?? string.Empty,
                Name = f.Name ?? string.Empty,
                MimeType = f.MimeType ?? string.Empty,
                Size = f.Size,
                IconLink = f.IconLink,
                WebViewLink = f.WebViewLink
            }).ToList()
        };
        await SendAsync(response, cancellation: ct);
    }
} 