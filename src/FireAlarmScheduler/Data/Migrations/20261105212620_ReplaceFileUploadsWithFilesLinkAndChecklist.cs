using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireAlarmScheduler.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFileUploadsWithFilesLinkAndChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadedFiles");

            migrationBuilder.AddColumn<string>(
                name: "FilesUrl",
                table: "Jobs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "Buildings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByTechnicianId",
                table: "Buildings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompletedByTechnicianId",
                table: "Buildings",
                column: "CompletedByTechnicianId");

            migrationBuilder.AddForeignKey(
                name: "FK_Buildings_Technicians_CompletedByTechnicianId",
                table: "Buildings",
                column: "CompletedByTechnicianId",
                principalTable: "Technicians",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Buildings_Technicians_CompletedByTechnicianId",
                table: "Buildings");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CompletedByTechnicianId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "FilesUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "CompletedByTechnicianId",
                table: "Buildings");

            migrationBuilder.CreateTable(
                name: "UploadedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadedFiles_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_BuildingId",
                table: "UploadedFiles",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_StoredFileName",
                table: "UploadedFiles",
                column: "StoredFileName",
                unique: true);
        }
    }
}
