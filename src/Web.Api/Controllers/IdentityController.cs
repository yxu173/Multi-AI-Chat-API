using System.Security.Claims;
using Application.Abstractions.Authentication;
using Application.Features.Identity.Login;
using Application.Features.Identity.Logout;
using Application.Features.Identity.Register;
using Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Contracts;
using Web.Api.Contracts.Identity;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class IdentityController : BaseController
{
    private readonly SignInManager<User> _signInManager;

    private readonly UserManager<User> _userManager;

    private readonly ITokenProvider _tokenProvider;

    public IdentityController(SignInManager<User> signInManager, UserManager<User> userManager,
        ITokenProvider tokenProvider)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokenProvider = tokenProvider;
    }

    [HttpPost("Register")]
    public async Task<IResult> Register([FromBody] RegisterCreate model)
    {
        var command = new RegisterUserCommand(
            model.UserName,
            model.Email,
            model.FirstName,
            model.LastName,
            model.Password
        );

        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPost("Login")]
    public async Task<IResult> Login([FromBody] LoginCreate model)
    {
        var command = new LoginUserCommand(
            model.Email,
            model.Password
        );

        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPost("Logout")]
    [Authorize]
    public async Task<IResult> Logout()
    {
        Result<bool> result = await _mediator.Send(new LogoutUserCommand());
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("google-login")]
    public IActionResult GoogleLogin()
    {
        var redirectUrl = "/signin-google";
        var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
        return Challenge(properties, "Google");
    }

    [HttpGet("github-login")]
    public IActionResult GithubLogin()
    {
        var redirectUrl = Url.Action("ExternalCallback", "Identity"); 
        var properties = _signInManager.ConfigureExternalAuthenticationProperties("GitHub", redirectUrl);
        return Challenge(properties, "GitHub");
    }

    [HttpGet("external-callback")]
    public async Task<IActionResult> ExternalCallback()
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return BadRequest("Error: External login info not found.");
        }

        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false
        );

        if (result.Succeeded)
        {
            var innerUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            var tokens = _tokenProvider.Create(innerUser);
            return Ok(new { Token = tokens });
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            return BadRequest("Email claim not found.");
        }

        var givenName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
        var surname = info.Principal.FindFirstValue(ClaimTypes.Surname);
        var name = info.Principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrEmpty(givenName) || string.IsNullOrEmpty(surname))
        {
            var nameParts = name?.Split(' ') ?? Array.Empty<string>();
            givenName = nameParts.Length > 0 ? nameParts[0] : "User";
            surname = nameParts.Length > 1 ? nameParts[1] : "Name";
        }

        
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = Domain.Users.User.Create(
                givenName,
                surname,
                email,
                givenName
            );

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return BadRequest("User creation failed: " +
                                  string.Join(", ", createResult.Errors.Select(e => e.Description)));
            }
        }

        // Link the external login
        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            return BadRequest("Failed to add external login: " +
                              string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        var token = _tokenProvider.Create(user);
        return Ok(new { Token = token });
    }
}