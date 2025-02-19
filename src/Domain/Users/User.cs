using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Domain.Users;

public sealed class User : IdentityUser<Guid>
{
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
}