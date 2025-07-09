using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Application.Services.Files;

public class GoogleDriveService
{
    private readonly UserManager<Domain.Aggregates.Users.User> _userManager;
    private readonly IConfiguration _config;

    public GoogleDriveService(UserManager<Domain.Aggregates.Users.User> userManager, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
    }

    public async Task<DriveService?> GetDriveServiceForUserAsync(Domain.Aggregates.Users.User user, CancellationToken ct)
    {
        var accessToken = await _userManager.GetAuthenticationTokenAsync(user, "GoogleDrive", "access_token");
        var refreshToken = await _userManager.GetAuthenticationTokenAsync(user, "GoogleDrive", "refresh_token");
        var expiresAtStr = await _userManager.GetAuthenticationTokenAsync(user, "GoogleDrive", "expires_at");
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            return null;
        DateTime.TryParse(expiresAtStr, out var expiresAt);
        if (expiresAt < DateTime.UtcNow.AddMinutes(-5))
        {
            var newTokens = await RefreshAccessTokenAsync(refreshToken, ct);
            if (newTokens != null)
            {
                accessToken = newTokens.Value.AccessToken;
                await _userManager.SetAuthenticationTokenAsync(user, "GoogleDrive", "access_token", newTokens.Value.AccessToken);
                await _userManager.SetAuthenticationTokenAsync(user, "GoogleDrive", "expires_at", newTokens.Value.ExpiresAt.ToString("o"));
            }
            else
            {
                return null;
            }
        }
        var cred = GoogleCredential.FromAccessToken(accessToken);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = _config["GoogleOAuth:ApplicationName"] ?? "Multi-AI-Chat-API"
        });
    }

    public async Task<(string AccessToken, DateTime ExpiresAt)?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        var clientId = _config["GoogleOAuth:ClientId"];
        var clientSecret = _config["GoogleOAuth:ClientSecret"];
        var tokenUrl = "https://oauth2.googleapis.com/token";
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("grant_type", "refresh_token")
        });
        var resp = await http.PostAsync(tokenUrl, content, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
        return (accessToken!, expiresAt);
    }

    public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(Domain.Aggregates.Users.User user, CancellationToken ct)
    {
        var drive = await GetDriveServiceForUserAsync(user, ct);
        if (drive == null) return new List<Google.Apis.Drive.v3.Data.File>();
        var request = drive.Files.List();
        request.Fields = "files(id, name, mimeType, size, iconLink, webViewLink)";
        request.PageSize = 100;
        var result = await request.ExecuteAsync(ct);
        return result.Files ?? new List<Google.Apis.Drive.v3.Data.File>();
    }
} 