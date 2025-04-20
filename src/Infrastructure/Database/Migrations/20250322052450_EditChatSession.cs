using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxInputTokens",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "MaxOutputTokens",
                table: "UserAiModelSettings");

            migrationBuilder.AddColumn<Guid>(
                name: "AiAgentId",
                table: "ChatSessions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAgentId",
                table: "ChatSessions");

            migrationBuilder.AddColumn<int>(
                name: "MaxInputTokens",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOutputTokens",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true);
        }
    }
}
