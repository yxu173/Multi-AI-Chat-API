using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class DeleteStopSequenceAndIconUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StopSequences",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "IconUrl",
                table: "AiAgents");

            migrationBuilder.DropColumn(
                name: "StopSequences",
                table: "AiAgents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StopSequences",
                table: "UserAiModelSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IconUrl",
                table: "AiAgents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StopSequences",
                table: "AiAgents",
                type: "text",
                nullable: true);
        }
    }
}
