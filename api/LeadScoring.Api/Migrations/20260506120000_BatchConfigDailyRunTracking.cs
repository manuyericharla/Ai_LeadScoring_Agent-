using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class BatchConfigDailyRunTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyRunCount",
                table: "BatchConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DailyRunCountDateUtc",
                table: "BatchConfigs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDailyRunUtc",
                table: "BatchConfigs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyRunCount",
                table: "BatchConfigs");

            migrationBuilder.DropColumn(
                name: "DailyRunCountDateUtc",
                table: "BatchConfigs");

            migrationBuilder.DropColumn(
                name: "LastDailyRunUtc",
                table: "BatchConfigs");
        }
    }
}
