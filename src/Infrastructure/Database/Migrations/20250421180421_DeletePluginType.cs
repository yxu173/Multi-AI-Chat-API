using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class DeletePluginType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParametersSchema",
                table: "Plugins");

            migrationBuilder.DropColumn(
                name: "PluginType",
                table: "Plugins");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParametersSchema",
                table: "Plugins",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PluginType",
                table: "Plugins",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
