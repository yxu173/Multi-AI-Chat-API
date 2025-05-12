using System;
using System.Threading.Tasks;
using Infrastructure.Database.Migrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Web.Api.Extensions;

public static class AdminApiKeyExtensions
{
    public static async Task<WebApplication> UseAdminManagedApiKeysAsync(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                logger.LogInformation("Running admin API key migration and seeding");
                await AdminApiKeyMigrationScript.RunMigrationAsync(app);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during admin API key migration");
            }
        }

        return app;
    }
}
