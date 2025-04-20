using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditUserAiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAiModelSettings_UserAiModels_UserAiModelId",
                table: "UserAiModelSettings");

            migrationBuilder.DropIndex(
                name: "IX_UserAiModelSettings_UserAiModelId",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "AiModelId",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "UserAiModelId",
                table: "UserAiModelSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiModelId",
                table: "UserAiModelSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserAiModelId",
                table: "UserAiModelSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModelSettings_UserAiModelId",
                table: "UserAiModelSettings",
                column: "UserAiModelId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAiModelSettings_UserAiModels_UserAiModelId",
                table: "UserAiModelSettings",
                column: "UserAiModelId",
                principalTable: "UserAiModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
