using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditChatToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatTokenUsages_Messages_MessageId",
                table: "ChatTokenUsages");

            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "ChatTokenUsages",
                newName: "ChatId");

            migrationBuilder.RenameIndex(
                name: "IX_ChatTokenUsages_MessageId",
                table: "ChatTokenUsages",
                newName: "IX_ChatTokenUsages_ChatId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatTokenUsages_ChatSessions_ChatId",
                table: "ChatTokenUsages",
                column: "ChatId",
                principalTable: "ChatSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatTokenUsages_ChatSessions_ChatId",
                table: "ChatTokenUsages");

            migrationBuilder.RenameColumn(
                name: "ChatId",
                table: "ChatTokenUsages",
                newName: "MessageId");

            migrationBuilder.RenameIndex(
                name: "IX_ChatTokenUsages_ChatId",
                table: "ChatTokenUsages",
                newName: "IX_ChatTokenUsages_MessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatTokenUsages_Messages_MessageId",
                table: "ChatTokenUsages",
                column: "MessageId",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
