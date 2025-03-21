using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Domain.DomainErrors;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using System.Collections.Generic;

namespace Application.Features.Identity.Register;

internal sealed class RegisterUserCommandHandler(
    UserManager<User> userManager,
    IUserRepository userRepository,
    ITokenProvider tokenProvider,
    IAiModelRepository aiModelRepository,
    IUserAiModelSettingsRepository userAiModelSettingsRepository)
    : ICommandHandler<RegisterUserCommand, string>
{
    public async Task<Result<string>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (await userRepository.ExistsByEmailAsync(command.Email))
        {
            return Result.Failure<string>(UserErrors.EmailNotUnique);
        }

        var user = User.Create(command.Email, command.UserName);
        if (user.IsFailure)
        {
            return Result.Failure<string>(user.Error);
        }

        var result = await userManager.CreateAsync(user.Value, command.Password);
        if (!result.Succeeded)
        {
            return Result.Failure<string>(UserErrors.RegisterUserError);
        }

        // Create default settings for all enabled AI models
        await CreateDefaultUserSettings(user.Value.Id, cancellationToken);

        var token = tokenProvider.Create(user.Value);

        return token;
    }

    private async Task CreateDefaultUserSettings(Guid userId, CancellationToken cancellationToken)
    {
        var enabledModels = await aiModelRepository.GetEnabledAsync();

        if (enabledModels.Count == 0)
        {
            return;
        }


        var settings = Domain.Aggregates.Users.UserAiModelSettings.Create(
            userId
        );

        await userAiModelSettingsRepository.AddAsync(settings, cancellationToken);
    }
}