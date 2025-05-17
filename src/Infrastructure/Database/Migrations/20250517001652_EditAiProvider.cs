using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditAiProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastUsed",
                table: "ProviderApiKeys",
                newName: "RateLimitedUntil");

            migrationBuilder.RenameColumn(
                name: "DailyUsage",
                table: "ProviderApiKeys",
                newName: "UsageCountToday");

            migrationBuilder.RenameColumn(
                name: "DailyQuota",
                table: "ProviderApiKeys",
                newName: "MaxRequestsPerDay");

            migrationBuilder.AddColumn<bool>(
                name: "IsRateLimited",
                table: "ProviderApiKeys",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedTimestamp",
                table: "ProviderApiKeys",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRateLimited",
                table: "ProviderApiKeys");

            migrationBuilder.DropColumn(
                name: "LastUsedTimestamp",
                table: "ProviderApiKeys");

            migrationBuilder.RenameColumn(
                name: "UsageCountToday",
                table: "ProviderApiKeys",
                newName: "DailyUsage");

            migrationBuilder.RenameColumn(
                name: "RateLimitedUntil",
                table: "ProviderApiKeys",
                newName: "LastUsed");

            migrationBuilder.RenameColumn(
                name: "MaxRequestsPerDay",
                table: "ProviderApiKeys",
                newName: "DailyQuota");
        }
    }
}
