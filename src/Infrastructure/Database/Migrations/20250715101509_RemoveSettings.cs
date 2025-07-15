using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextLimit",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "FrequencyPenalty",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "PresencePenalty",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "PromptCaching",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "SafetySettings",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "TopK",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "TopP",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "ContextLimit",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "FrequencyPenalty",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "PresencePenalty",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "PromptCaching",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "SafetySettings",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "TopK",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "TopP",
                table: "AiAgents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContextLimit",
                table: "UserAiModelSettings",
                type: "integer",
                maxLength: 100,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "FrequencyPenalty",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PresencePenalty",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "PromptCaching",
                table: "UserAiModelSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SafetySettings",
                table: "UserAiModelSettings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopK",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "TopP",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ContextLimit",
                table: "AiAgents",
                type: "integer",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FrequencyPenalty",
                table: "AiAgents",
                type: "double precision",
                precision: 3,
                scale: 2,
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

            migrationBuilder.AddColumn<string>(
                name: "SafetySettings",
                table: "AiAgents",
                type: "character varying(1000)",
                maxLength: 1000,
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
        }
    }
}
