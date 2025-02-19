using Microsoft.AspNetCore.Identity;

namespace Domain.Users;

public sealed class User : IdentityUser<Guid>
{
    private User()
    {
    }

    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Provider { get; set; }
    public string? ProviderUserId { get; set; }

    public static User Create(string firstName, string lastName, string email, string userName)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            UserName = userName
        };
    }
}