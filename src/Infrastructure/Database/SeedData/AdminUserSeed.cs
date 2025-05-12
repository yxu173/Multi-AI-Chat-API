using Domain.Aggregates.Users;
using Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Database.SeedData;

public static class AdminUserSeed
{
    public static async Task SeedAdminUserAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        try
        {
            if (!await roleManager.RoleExistsAsync(UserRoles.Admin))
            {
                await roleManager.CreateAsync(Role.Create(UserRoles.Admin));
                logger.LogInformation("Created Admin role");
            }

            if (!await roleManager.RoleExistsAsync(UserRoles.User))
            {
                await roleManager.CreateAsync(Role.Create(UserRoles.User));
                logger.LogInformation("Created User role");
            }

            // Check if admin user exists
            const string adminEmail = "admin@example.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            
            if (adminUser == null)
            {
                var user = User.Create(adminEmail, "Admin").Value;
                
                var result = await userManager.CreateAsync(user, "Admin123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, UserRoles.Admin);
                    logger.LogInformation("Created admin user with email: {Email}", adminEmail);
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        logger.LogError("Error creating admin user: {Error}", error.Description);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding admin user");
        }
    }
}
