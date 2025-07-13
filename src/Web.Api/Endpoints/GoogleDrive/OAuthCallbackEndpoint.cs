using FastEndpoints;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Domain.Aggregates.Users;
using System.Security.Claims;

namespace Web.Api.Endpoints.GoogleDrive;

public class OAuthCallbackEndpoint : EndpointWithoutRequest
{
    private readonly IConfiguration _config;
    private readonly UserManager<User> _userManager;

    public OAuthCallbackEndpoint(IConfiguration config, UserManager<User> userManager)
    {
        _config = config;
        _userManager = userManager;
    }

    public override void Configure()
    {
        Get("/api/google-drive/auth/callback");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = HttpContext.Request.Query["code"].ToString();
        if (string.IsNullOrEmpty(code))
        {
            await SendStringAsync("No code provided", 400, cancellation: ct);
            return;
        }

        // var state = HttpContext.Request.Query["state"].ToString();
        // if (string.IsNullOrEmpty(state) || !Guid.TryParse(state, out var userId))
        // {
        //     await SendStringAsync("Missing or invalid state/user", 400, cancellation: ct);
        //     return;
        // }
        
        var userId = new Guid("0197f3e8-bc1c-7595-8a79-02737428d931");
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendStringAsync("User not found", 404, cancellation: ct);
            return;
        }

        var clientId = _config["GoogleOAuth:ClientId"];
        var clientSecret = _config["GoogleOAuth:ClientSecret"];
        var redirectUri = _config["GoogleOAuth:RedirectUri"];

        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
        });

        var response = await http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            await SendStringAsync("Failed to get tokens: " + json, 400, cancellation: ct);
            return;
        }

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 0;
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

        await _userManager.SetAuthenticationTokenAsync(user, "GoogleDrive", "access_token", accessToken);
        if (!string.IsNullOrEmpty(refreshToken))
            await _userManager.SetAuthenticationTokenAsync(user, "GoogleDrive", "refresh_token", refreshToken);
        await _userManager.SetAuthenticationTokenAsync(user, "GoogleDrive", "expires_at", expiresAt.ToString("o"));

        await SendStringAsync("Google Drive connected! You can close this window.", cancellation: ct);
    }
} 