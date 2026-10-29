using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireAlarmScheduler.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringJobBuildings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringJobBuildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecurringJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringJobBuildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringJobBuildings_RecurringJobs_RecurringJobId",
                        column: x => x.RecurringJobId,
                        principalTable: "RecurringJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing recurring jobs start with the building list of their newest job (by start date and time).
            migrationBuilder.Sql("""
                INSERT INTO RecurringJobBuildings (Id, RecurringJobId, Name, Notes, SortOrder)
                SELECT NEWID(), j.RecurringJobId, b.Name, b.Notes,
                       ROW_NUMBER() OVER (PARTITION BY j.RecurringJobId ORDER BY b.SortOrder) - 1
                FROM Buildings b
                JOIN Jobs j ON b.JobId = j.Id
                WHERE j.RecurringJobId IS NOT NULL
                  AND j.Id = (SELECT TOP 1 j2.Id FROM Jobs j2
                              WHERE j2.RecurringJobId = j.RecurringJobId
                              ORDER BY j2.StartDate DESC, j2.StartTime DESC);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJobBuildings_RecurringJobId",
                table: "RecurringJobBuildings",
                column: "RecurringJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringJobBuildings");
        }
    }
}
