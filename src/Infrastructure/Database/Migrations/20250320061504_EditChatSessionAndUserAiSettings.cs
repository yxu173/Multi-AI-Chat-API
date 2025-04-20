using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditChatSessionAndUserAiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "StopSequences",
                table: "UserAiModelSettings",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "SystemPrompt",
                table: "ChatSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AssignCustomModelParameters",
                table: "AiAgents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "Categories",
                table: "AiAgents",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ModelParameters",
                table: "AiAgents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureUrl",
                table: "AiAgents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StopSequences",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "SystemPrompt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "AssignCustomModelParameters",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "Categories",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "ModelParameters",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "ProfilePictureUrl",
                table: "AiAgents");
        }
    }
}
