using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChatPLugins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatSessionPlugins");

            migrationBuilder.DropColumn(
                name: "CustomApiKey",
                table: "ChatSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomApiKey",
                table: "ChatSessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatSessionPlugins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessionPlugins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatSessionPlugins_ChatSessions_ChatSessionId",
                        column: x => x.ChatSessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatSessionPlugins_Plugins_PluginId",
                        column: x => x.PluginId,
                        principalTable: "Plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionPlugins_ChatSessionId",
                table: "ChatSessionPlugins",
                column: "ChatSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionPlugins_PluginId",
                table: "ChatSessionPlugins",
                column: "PluginId");
        }
    }
}
