using Microsoft.AspNetCore.Identity;

namespace Domain.Users;

public sealed class Role : IdentityRole<Guid>
{
    public static Role Create(string roleName)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant()
        };
    }
}