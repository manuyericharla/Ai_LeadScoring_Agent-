using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class DailySequenceBatchReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WelcomeEmailSent",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "Leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmailSentDateUtc",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextEmailSendDateUtc",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminBatchReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Stage0Count = table.Column<int>(type: "integer", nullable: false),
                    Stage1Count = table.Column<int>(type: "integer", nullable: false),
                    Stage2Count = table.Column<int>(type: "integer", nullable: false),
                    Stage3Count = table.Column<int>(type: "integer", nullable: false),
                    Stage4Count = table.Column<int>(type: "integer", nullable: false),
                    BatchDailyCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminBatchReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BatchLogs",
                columns: table => new
                {
                    BatchId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BatchType = table.Column<int>(type: "integer", nullable: false),
                    TotalLeadsProcessed = table.Column<int>(type: "integer", nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchLogs", x => x.BatchId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_LastEmailSentDateUtc",
                table: "Leads",
                column: "LastEmailSentDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Stage_CreatedAtUtc",
                table: "Leads",
                columns: new[] { "Stage", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WelcomeEmailSent_LastActivityUtc",
                table: "Leads",
                columns: new[] { "WelcomeEmailSent", "LastActivityUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminBatchReports_Email",
                table: "AdminBatchReports",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_RunDate",
                table: "BatchLogs",
                column: "RunDate");

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_RunDate_BatchType",
                table: "BatchLogs",
                columns: new[] { "RunDate", "BatchType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminBatchReports");

            migrationBuilder.DropTable(
                name: "BatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_Leads_LastEmailSentDateUtc",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_Stage_CreatedAtUtc",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_WelcomeEmailSent_LastActivityUtc",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LastEmailSentDateUtc",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "NextEmailSendDateUtc",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "WelcomeEmailSent",
                table: "Leads");
        }
    }
}
