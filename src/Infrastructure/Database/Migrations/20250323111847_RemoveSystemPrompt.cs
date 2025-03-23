using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSystemPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemPrompt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "SystemPrompt",
                table: "AiAgents");

            migrationBuilder.AddColumn<string>(
                name: "SystemMessage",
                table: "UserAiModelSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemInstructions",
                table: "AiAgents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemMessage",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "SystemInstructions",
                table: "AiAgents");

            migrationBuilder.AddColumn<string>(
                name: "SystemPrompt",
                table: "ChatSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemPrompt",
                table: "AiAgents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
