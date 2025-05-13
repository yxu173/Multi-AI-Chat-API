using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedAttributesFromAiModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiType",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "MaxInputTokens",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "StreamingOutputSupported",
                table: "AiModels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiType",
                table: "AiModels",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxInputTokens",
                table: "AiModels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StreamingOutputSupported",
                table: "AiModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
