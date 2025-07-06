using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNewIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_CreatedAt",
                table: "UserApiKeys",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_LastUsed",
                table: "UserApiKeys",
                column: "LastUsed");

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_UserId_AiProviderId",
                table: "UserApiKeys",
                columns: new[] { "UserId", "AiProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_UserId_CreatedAt",
                table: "UserApiKeys",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModels_IsEnabled",
                table: "UserAiModels",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModels_UserId_AiModelId",
                table: "UserAiModels",
                columns: new[] { "UserId", "AiModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModels_UserId_IsEnabled",
                table: "UserAiModels",
                columns: new[] { "UserId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatSessionId_CreatedAt",
                table: "Messages",
                columns: new[] { "ChatSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatSessionId_IsFromAi",
                table: "Messages",
                columns: new[] { "ChatSessionId", "IsFromAi" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_UserId",
                table: "Messages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_UserId_CreatedAt",
                table: "Messages",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTokenUsages_ChatId_CreatedAt",
                table: "ChatTokenUsages",
                columns: new[] { "ChatId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTokenUsages_CreatedAt",
                table: "ChatTokenUsages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatTokenUsages_TotalCost",
                table: "ChatTokenUsages",
                column: "TotalCost");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_ChatType",
                table: "ChatSessions",
                columns: new[] { "UserId", "ChatType" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_CreatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_FolderId",
                table: "ChatSessions",
                columns: new[] { "UserId", "FolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_AiProviderId_IsEnabled",
                table: "AiModels",
                columns: new[] { "AiProviderId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_IsEnabled",
                table: "AiModels",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_ModelCode",
                table: "AiModels",
                column: "ModelCode");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_ModelType",
                table: "AiModels",
                column: "ModelType");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_ModelType_IsEnabled",
                table: "AiModels",
                columns: new[] { "ModelType", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserApiKeys_CreatedAt",
                table: "UserApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_UserApiKeys_LastUsed",
                table: "UserApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_UserApiKeys_UserId_AiProviderId",
                table: "UserApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_UserApiKeys_UserId_CreatedAt",
                table: "UserApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_UserAiModels_IsEnabled",
                table: "UserAiModels");

            migrationBuilder.DropIndex(
                name: "IX_UserAiModels_UserId_AiModelId",
                table: "UserAiModels");

            migrationBuilder.DropIndex(
                name: "IX_UserAiModels_UserId_IsEnabled",
                table: "UserAiModels");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatSessionId_CreatedAt",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatSessionId_IsFromAi",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_UserId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_UserId_CreatedAt",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ChatTokenUsages_ChatId_CreatedAt",
                table: "ChatTokenUsages");

            migrationBuilder.DropIndex(
                name: "IX_ChatTokenUsages_CreatedAt",
                table: "ChatTokenUsages");

            migrationBuilder.DropIndex(
                name: "IX_ChatTokenUsages_TotalCost",
                table: "ChatTokenUsages");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_ChatType",
                table: "ChatSessions");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_CreatedAt",
                table: "ChatSessions");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_FolderId",
                table: "ChatSessions");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_AiProviderId_IsEnabled",
                table: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_IsEnabled",
                table: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_ModelCode",
                table: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_ModelType",
                table: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_ModelType_IsEnabled",
                table: "AiModels");
        }
    }
}
