using Domain.DomainErrors;
using Microsoft.AspNetCore.Identity;
using SharedKernal;

namespace Domain.Aggregates.Users;

public sealed class User : IdentityUser<Guid>
{
    private readonly List<UserPlugin> _userPlugins = new();
    private readonly List<UserAiModel> _userAiModels = new();

    public IReadOnlyList<UserAiModel> UserAiModels => _userAiModels.AsReadOnly();
    public IReadOnlyList<UserPlugin> UserPlugins => _userPlugins.AsReadOnly();

    private User()
    {
    }

    private User(string email, string userName)
    {
        Email = email;
        UserName = userName;
    }


    public static Result<User> Create(string email, string userName)
    {
        try
        {
            return Result.Success(new User(email.Trim(), userName.Trim()));
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<User>(Error.Validation("User.Create", ex.Message));
        }
    }

    public Result AddAiModel(UserAiModel userAiModel)
    {
        if (_userAiModels.Any(x => x.AiModelId == userAiModel.AiModelId))
            return Result.Failure(UserErrors.AiModelIsAlreadyExist);

        _userAiModels.Add(userAiModel);
        return Result.Success();
    }
}