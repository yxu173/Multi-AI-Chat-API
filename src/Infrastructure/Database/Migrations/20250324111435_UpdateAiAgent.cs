using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAiAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAgentPlugins_AiAgentId",
                table: "AiAgentPlugins");

            migrationBuilder.RenameColumn(
                name: "ModelParameters",
                table: "AiAgents",
                newName: "StopSequences");

            migrationBuilder.AddColumn<bool>(
                name: "EnableThinking",
                table: "UserAiModelSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Base64Content",
                table: "FileAttachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "FileAttachments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "FileAttachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FileType",
                table: "FileAttachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "EnableThinking",
                table: "ChatSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsThinking",
                table: "AiModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "SystemInstructions",
                table: "AiAgents",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProfilePictureUrl",
                table: "AiAgents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AiAgents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "IconUrl",
                table: "AiAgents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "AiAgents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Categories",
                table: "AiAgents",
                type: "text",
                nullable: false,
                oldClrType: typeof(List<string>),
                oldType: "text[]");

            migrationBuilder.AlterColumn<bool>(
                name: "AssignCustomModelParameters",
                table: "AiAgents",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<string>(
                name: "ContextLimit",
                table: "AiAgents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableThinking",
                table: "AiAgents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FrequencyPenalty",
                table: "AiAgents",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxTokens",
                table: "AiAgents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PresencePenalty",
                table: "AiAgents",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PromptCaching",
                table: "AiAgents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReasoningEffort",
                table: "AiAgents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetySettings",
                table: "AiAgents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Temperature",
                table: "AiAgents",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopK",
                table: "AiAgents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TopP",
                table: "AiAgents",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "AiAgentPlugins",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.CreateIndex(
                name: "IX_AiAgentPlugins_AiAgentId_PluginId",
                table: "AiAgentPlugins",
                columns: new[] { "AiAgentId", "PluginId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAgentPlugins_AiAgentId_PluginId",
                table: "AiAgentPlugins");

            migrationBuilder.DropColumn(
                name: "EnableThinking",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "Base64Content",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "FileType",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "EnableThinking",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "SupportsThinking",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "ContextLimit",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "EnableThinking",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "FrequencyPenalty",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "MaxTokens",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "PresencePenalty",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "PromptCaching",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "ReasoningEffort",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "SafetySettings",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "Temperature",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "TopK",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "TopP",
                table: "AiAgents");

            migrationBuilder.RenameColumn(
                name: "StopSequences",
                table: "AiAgents",
                newName: "ModelParameters");

            migrationBuilder.AlterColumn<string>(
                name: "SystemInstructions",
                table: "AiAgents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProfilePictureUrl",
                table: "AiAgents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AiAgents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "IconUrl",
                table: "AiAgents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "AiAgents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<List<string>>(
                name: "Categories",
                table: "AiAgents",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<bool>(
                name: "AssignCustomModelParameters",
                table: "AiAgents",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "AiAgentPlugins",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAgentPlugins_AiAgentId",
                table: "AiAgentPlugins",
                column: "AiAgentId");
        }
    }
}
