using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Web.Api.Endpoints.GoogleDrive;

public class StartGoogleOAuthEndpoint : EndpointWithoutRequest
{
    private readonly IConfiguration _config;

    public StartGoogleOAuthEndpoint(IConfiguration config)
    {
        _config = config;
    }

    public override void Configure()
    {
        Get("/api/google-drive/auth/start");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var clientId = _config["GoogleOAuth:ClientId"];
        var redirectUri = _config["GoogleOAuth:RedirectUri"];
        var scopes = "https://www.googleapis.com/auth/drive.readonly";

        // Get user ID from JWT
        var userIdStr = "0197f3e8-bc1c-7595-8a79-02737428d931";
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            await SendStringAsync("User not authenticated", 401, cancellation: ct);
            return;
        }

        var url = $"https://accounts.google.com/o/oauth2/v2/auth" +
                  $"?client_id={clientId}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&response_type=code" +
                  $"&scope={Uri.EscapeDataString(scopes)}" +
                  $"&access_type=offline" +
                  $"&prompt=consent" +
                  $"&state={userId}";

        await SendRedirectAsync(url, false, true);
    }
} 