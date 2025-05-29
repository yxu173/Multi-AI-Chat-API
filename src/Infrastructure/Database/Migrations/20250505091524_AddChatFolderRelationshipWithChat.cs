using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFolderRelationshipWithChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_ChatFolders_FolderId",
                table: "ChatSessions");

            migrationBuilder.AlterColumn<int>(
                name: "ContextLength",
                table: "AiModels",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_ChatFolders_FolderId",
                table: "ChatSessions",
                column: "FolderId",
                principalTable: "ChatFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_ChatFolders_FolderId",
                table: "ChatSessions");

            migrationBuilder.AlterColumn<int>(
                name: "ContextLength",
                table: "AiModels",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_ChatFolders_FolderId",
                table: "ChatSessions",
                column: "FolderId",
                principalTable: "ChatFolders",
                principalColumn: "Id");
        }
    }
}
