using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLargeFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Checksum",
                table: "FileAttachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "LargeFileUploadId",
                table: "FileAttachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "FileAttachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "FileAttachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UploadStatus",
                table: "FileAttachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LargeFileUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalSize = table.Column<long>(type: "bigint", nullable: false),
                    TotalParts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TargetDirectory = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FinalFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LargeFileUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadPart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<int>(type: "integer", nullable: false),
                    PartPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LargeFileUploadId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadPart", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadPart_LargeFileUploads_LargeFileUploadId",
                        column: x => x.LargeFileUploadId,
                        principalTable: "LargeFileUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadPart_LargeFileUploadId_PartNumber",
                table: "UploadPart",
                columns: new[] { "LargeFileUploadId", "PartNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadPart");

            migrationBuilder.DropTable(
                name: "LargeFileUploads");

            migrationBuilder.DropColumn(
                name: "Checksum",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "LargeFileUploadId",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "UploadStatus",
                table: "FileAttachments");
        }
    }
}
