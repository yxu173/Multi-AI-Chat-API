using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class DeleteUnusedFieldsAiModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PluginsSupported",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "SystemRoleSupported",
                table: "AiModels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PluginsSupported",
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
    }
}
