using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAgentAndUserSettingsAndChatFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "ChatSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    AiModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IconUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAgents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAgents_AiModels_AiModelId",
                        column: x => x.AiModelId,
                        principalTable: "AiModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAiModelSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxInputTokens = table.Column<int>(type: "integer", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: true),
                    Temperature = table.Column<double>(type: "double precision", nullable: true),
                    TopP = table.Column<double>(type: "double precision", nullable: true),
                    TopK = table.Column<int>(type: "integer", nullable: true),
                    FrequencyPenalty = table.Column<double>(type: "double precision", nullable: true),
                    PresencePenalty = table.Column<double>(type: "double precision", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    UserAiModelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiModelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiModelSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAiModelSettings_UserAiModels_UserAiModelId",
                        column: x => x.UserAiModelId,
                        principalTable: "UserAiModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiAgentPlugins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AiAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAgentPlugins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAgentPlugins_AiAgents_AiAgentId",
                        column: x => x.AiAgentId,
                        principalTable: "AiAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiAgentPlugins_Plugins_PluginId",
                        column: x => x.PluginId,
                        principalTable: "Plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_FolderId",
                table: "ChatSessions",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAgentPlugins_AiAgentId",
                table: "AiAgentPlugins",
                column: "AiAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAgentPlugins_PluginId",
                table: "AiAgentPlugins",
                column: "PluginId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAgents_AiModelId",
                table: "AiAgents",
                column: "AiModelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModelSettings_UserAiModelId",
                table: "UserAiModelSettings",
                column: "UserAiModelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModelSettings_UserId",
                table: "UserAiModelSettings",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_ChatFolders_FolderId",
                table: "ChatSessions",
                column: "FolderId",
                principalTable: "ChatFolders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_ChatFolders_FolderId",
                table: "ChatSessions");

            migrationBuilder.DropTable(
                name: "AiAgentPlugins");

            migrationBuilder.DropTable(
                name: "ChatFolders");

            migrationBuilder.DropTable(
                name: "UserAiModelSettings");

            migrationBuilder.DropTable(
                name: "AiAgents");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_FolderId",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "ChatSessions");
        }
    }
}
