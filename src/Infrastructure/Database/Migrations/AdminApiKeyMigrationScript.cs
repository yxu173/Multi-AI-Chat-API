using System;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;
using Domain.Aggregates.Users;
using Domain.Constants;
using Infrastructure.Database.SeedData;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Database.Migrations;

/// <summary>
/// Helper class to run the admin API key and subscription migration programmatically.
/// </summary>
public static class AdminApiKeyMigrationScript
{
    /// <summary>
    /// Runs the migration and seeding for the admin-managed API key and subscription system.
    /// </summary>
    public static async Task RunMigrationAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();
        var dbContext = services.GetRequiredService<ApplicationDbContext>();

        try
        {
            logger.LogInformation("Starting admin API key and subscription migration");

            // Run the migrations
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully");

            // Seed admin user and roles
            await AdminUserSeed.SeedAdminUserAsync(services);

            // Migrate existing user API keys to admin-managed API keys
            await MigrateExistingApiKeysAsync(dbContext, services, logger);

            logger.LogInformation("Admin API key and subscription migration completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during admin API key and subscription migration");
            throw;
        }
    }

    private static async Task MigrateExistingApiKeysAsync(
        ApplicationDbContext dbContext,
        IServiceProvider services,
        ILogger logger)
    {
        try
        {
            // Find admin user to set as creator of migrated keys
            var userManager = services.GetRequiredService<UserManager<User>>();
            var adminUsers = await userManager.GetUsersInRoleAsync(UserRoles.Admin);
            if (!adminUsers.Any())
            {
                logger.LogWarning("No admin users found for API key migration");
                return;
            }

            var adminId = adminUsers.First().Id;
            logger.LogInformation("Using admin user ID {AdminId} as creator for migrated API keys", adminId);

            // Get existing user API keys
            var existingUserApiKeys = await dbContext.UserApiKeys
                .Include(k => k.AiProvider)
                .ToListAsync();

            logger.LogInformation("Found {Count} user API keys to migrate", existingUserApiKeys.Count);

            // Group by provider to avoid duplicates
            var keysByProvider = existingUserApiKeys
                .GroupBy(k => k.AiProviderId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var providerGroup in keysByProvider)
            {
                var providerId = providerGroup.Key;
                var keys = providerGroup.Value;
                
                // Take the most recently used key for each provider
                var mostRecentKey = keys
                    .OrderByDescending(k => k.LastUsed ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (mostRecentKey != null)
                {
                    // Create new admin-managed key
                    var providerApiKey = ProviderApiKey.Create(
                        providerId,
                        mostRecentKey.ApiKey,
                        $"Migrated from user {mostRecentKey.UserId}",
                        adminId,
                        1000 // Default daily quota
                    );

                    await dbContext.ProviderApiKeys.AddAsync(providerApiKey);
                    logger.LogInformation("Created admin-managed key for provider {ProviderId}", providerId);
                }
            }

            await dbContext.SaveChangesAsync();
            logger.LogInformation("Successfully migrated user API keys to admin-managed API keys");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error migrating user API keys to admin-managed API keys");
            throw;
        }
    }
}
