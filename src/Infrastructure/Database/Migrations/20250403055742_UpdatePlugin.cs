using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePlugin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningEffort",
                table: "AiAgents");

            migrationBuilder.AddColumn<string>(
                name: "ParametersSchema",
                table: "Plugins",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParametersSchema",
                table: "Plugins");

            migrationBuilder.AddColumn<int>(
                name: "ReasoningEffort",
                table: "AiAgents",
                type: "integer",
                nullable: true);
        }
    }
}
