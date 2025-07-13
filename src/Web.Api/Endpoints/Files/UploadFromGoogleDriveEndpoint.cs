using Application.Services.Files;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Domain.Aggregates.Users;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System.Security.Claims;

namespace Web.Api.Endpoints.Files;


public class UploadFromGoogleDriveRequest
{
    public string GoogleFileId { get; set; } = string.Empty;
    public Guid? MessageId { get; set; }
}

public class UploadFromGoogleDriveResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public Guid? MessageId { get; set; }
}

[Authorize]
public class UploadFromGoogleDriveEndpoint : Endpoint<UploadFromGoogleDriveRequest, UploadFromGoogleDriveResponse>
{
    private readonly FileUploadService _fileUploadService;
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _config;

    public UploadFromGoogleDriveEndpoint(FileUploadService fileUploadService, UserManager<User> userManager, IConfiguration config)
    {
        _fileUploadService = fileUploadService;
        _userManager = userManager;
        _config = config;
    }

    public override void Configure()
    {
        Post("/api/file/upload-from-google-drive");
        Description(x => x
            .Produces(200, typeof(UploadFromGoogleDriveResponse))
            .Produces(400)
            .Produces(401)
            .Produces(500));
    }

    public override async Task HandleAsync(UploadFromGoogleDriveRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
        var accessToken = await _userManager.GetAuthenticationTokenAsync(user, "GoogleDrive", "access_token");
        var refreshToken = await _userManager.GetAuthenticationTokenAsync(user, "GoogleDrive", "refresh_token");
        var expiresAtStr = await _userManager.GetAuthenticationTokenAsync(user, "GoogleDrive", "expires_at");
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
        {
            AddError("Google Drive not connected. Please authenticate first.");
            await SendErrorsAsync(400, ct);
            return;
        }
        
        
        var cred = GoogleCredential.FromAccessToken(accessToken);
        var driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = _config["GoogleOAuth:ApplicationName"] ?? "Multi-AI-Chat-API"
        });
        
        
        var file = await driveService.Files.Get(req.GoogleFileId).ExecuteAsync(ct);
        var fileName = file.Name;
        var contentType = file.MimeType;
        
        // Validate file type
        if (!IsAllowedFileType(contentType))
        {
            AddError($"File type '{contentType}' is not allowed. Only PDF, CSV, plain text, and image files (JPEG, PNG, GIF, WebP) are supported.");
            await SendErrorsAsync(400, ct);
            return;
        }
        
        // Validate file size (30MB limit)
        if (file.Size > 30_000_000)
        {
            AddError("File size exceeds the limit (30MB)");
            await SendErrorsAsync(400, ct);
            return;
        }
        
        using var memStream = new MemoryStream();
        await driveService.Files.Get(req.GoogleFileId).DownloadAsync(memStream, ct);
        memStream.Position = 0;

        var formFile = new FormFile(memStream, 0, memStream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        var fileAttachment = await _fileUploadService.UploadFileAsync(formFile, req.MessageId, ct);
        await SendAsync(new UploadFromGoogleDriveResponse
        {
            Id = fileAttachment.Id,
            FileName = fileAttachment.FileName,
            ContentType = fileAttachment.ContentType,
            FileType = fileAttachment.FileType.ToString(),
            FileSize = fileAttachment.FileSize,
            MessageId = fileAttachment.MessageId
        }, cancellation: ct);
    }
    
    private bool IsAllowedFileType(string contentType)
    {   
        return contentType == "application/pdf" ||
               contentType == "text/csv" ||
               contentType == "application/csv" ||
               contentType == "text/plain" ||
               contentType == "image/jpeg" ||
               contentType == "image/png" ||
               contentType == "image/gif" ||
               contentType == "image/webp";
    }
} 