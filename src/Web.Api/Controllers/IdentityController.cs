using System.Security.Claims;
using Application.Abstractions.Authentication;
using Application.Features.Identity.ForgetPassword;
using Application.Features.Identity.Login;
using Application.Features.Identity.Logout;
using Application.Features.Identity.Register;
using Application.Features.Identity.ResetPassword;
using Domain.Aggregates.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
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

    [HttpPost("ForgotPassword")]
    public async Task<IResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var result = await _mediator.Send(new ForgetPasswordCommand(request.Email));
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPost("ResetPassword")]
    public async Task<IResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _mediator.Send(new ResetPasswordCommand(request.Email,
            request.ResetCode,
            request.NewPassword));
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("google-login")]
    public IActionResult GoogleLogin()
    {
        var redirectUrl = Url.Action("ExternalCallback", "Identity");
        var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
        return Challenge(properties, "Google");
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

        if (!result.Succeeded)
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email claim not found.");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                var givenName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
                var surname = info.Principal.FindFirstValue(ClaimTypes.Surname);
                var name = info.Principal.FindFirstValue(ClaimTypes.Name);

                if (string.IsNullOrEmpty(givenName) || string.IsNullOrEmpty(surname))
                {
                    var nameParts = name?.Split(' ') ?? Array.Empty<string>();
                    givenName = nameParts.Length > 0 ? nameParts[0] : "User";
                    surname = nameParts.Length > 1 ? nameParts[1] : "Name";
                }

                user = Domain.Aggregates.Users.User.Create(
                email,
                givenName
                ).Value;

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return BadRequest("User creation failed: " +
                        string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }

               // var roleResult = await _userManager.AddToRoleAsync(user, Roles.Basic.ToString());
                // if (!roleResult.Succeeded)
                // {
                //     return BadRequest("Role assignment failed: " +
                //         string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                // }
            }

            
            var addLoginResult = await _userManager.AddLoginAsync(user, info);
            if (!addLoginResult.Succeeded)
            {
                return BadRequest("Failed to add external login: " +
                    string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
        }

        var currentUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        var token = _tokenProvider.Create(currentUser);
        return Ok(new { Token = token });
    }

    [HttpGet("external-login")]
    public IActionResult ExternalLogin()
    {
        var redirectUrl = Url.Action("ExternalLoginCallback");
        return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, GoogleDefaults.AuthenticationScheme);
    }
}