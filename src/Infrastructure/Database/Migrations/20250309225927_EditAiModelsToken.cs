using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditAiModelsToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OutputTokenPricePer1K",
                table: "AiModels",
                newName: "OutputTokenPricePer1M");

            migrationBuilder.RenameColumn(
                name: "InputTokenPricePer1K",
                table: "AiModels",
                newName: "InputTokenPricePer1M");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OutputTokenPricePer1M",
                table: "AiModels",
                newName: "OutputTokenPricePer1K");

            migrationBuilder.RenameColumn(
                name: "InputTokenPricePer1M",
                table: "AiModels",
                newName: "InputTokenPricePer1K");
        }
    }
}
