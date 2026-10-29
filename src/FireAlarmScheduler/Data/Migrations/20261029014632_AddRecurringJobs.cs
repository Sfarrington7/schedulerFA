using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireAlarmScheduler.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurringJobId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_RecurringJobId",
                table: "Jobs",
                column: "RecurringJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_RecurringJobs_RecurringJobId",
                table: "Jobs",
                column: "RecurringJobId",
                principalTable: "RecurringJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_RecurringJobs_RecurringJobId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "RecurringJobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_RecurringJobId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "RecurringJobId",
                table: "Jobs");
        }
    }
}
