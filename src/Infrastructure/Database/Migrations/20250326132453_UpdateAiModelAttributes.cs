using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAiModelAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsDefault",
                table: "UserAiModelSettings",
                newName: "PromptCaching");

            migrationBuilder.AddColumn<int>(
                name: "ContextLimit",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultModel",
                table: "UserAiModelSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "MaxTokens",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetySettings",
                table: "UserAiModelSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiType",
                table: "AiModels",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ContextLength",
                table: "AiModels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PluginsSupported",
                table: "AiModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PromptCachingSupported",
                table: "AiModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StreamingOutputSupported",
                table: "AiModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SystemRoleSupported",
                table: "AiModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextLimit",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "DefaultModel",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "MaxTokens",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "SafetySettings",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "ApiType",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "ContextLength",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "PluginsSupported",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "PromptCachingSupported",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "StreamingOutputSupported",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "SystemRoleSupported",
                table: "AiModels");

            migrationBuilder.RenameColumn(
                name: "PromptCaching",
                table: "UserAiModelSettings",
                newName: "IsDefault");
        }
    }
}
