using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerraformLogViewer.Migrations
{
    /// <inheritdoc />
    public partial class AddFileTypeColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FileType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalEntries = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogFiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LogFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ParsedTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    RawMessage = table.Column<string>(type: "text", nullable: false),
                    TfReqId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TfResourceType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TfResourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    HttpReqBody = table.Column<string>(type: "jsonb", nullable: true),
                    HttpResBody = table.Column<string>(type: "jsonb", nullable: true),
                    HttpMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    HttpUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SourceFile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LineNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogEntries_LogFiles_LogFileId",
                        column: x => x.LogFileId,
                        principalTable: "LogFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Level",
                table: "LogEntries",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_LogFileId",
                table: "LogEntries",
                column: "LogFileId");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Phase",
                table: "LogEntries",
                column: "Phase");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Status",
                table: "LogEntries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_TfReqId",
                table: "LogEntries",
                column: "TfReqId");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_TfResourceType",
                table: "LogEntries",
                column: "TfResourceType");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Timestamp",
                table: "LogEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_LogFiles_UploadedAt",
                table: "LogFiles",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LogFiles_UserId",
                table: "LogFiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogEntries");

            migrationBuilder.DropTable(
                name: "LogFiles");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
