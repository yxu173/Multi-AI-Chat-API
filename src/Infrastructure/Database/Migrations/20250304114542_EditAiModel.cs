using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditAiModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiModels_AiProviders_AiProviderId1",
                table: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_AiProviderId1",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "AiProviderId1",
                table: "AiModels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiProviderId1",
                table: "AiModels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_AiProviderId1",
                table: "AiModels",
                column: "AiProviderId1");

            migrationBuilder.AddForeignKey(
                name: "FK_AiModels_AiProviders_AiProviderId1",
                table: "AiModels",
                column: "AiProviderId1",
                principalTable: "AiProviders",
                principalColumn: "Id");
        }
    }
}
