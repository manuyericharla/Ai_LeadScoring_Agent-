using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations;

/// <summary>
/// Repairs databases where DailySequenceBatchReporting ran before WelcomeEmailSent/ProductId were added to its Up().
/// </summary>
public partial class EnsureLeadWelcomeEmailSentColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "Leads" ADD COLUMN IF NOT EXISTS "WelcomeEmailSent" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Leads" ADD COLUMN IF NOT EXISTS "ProductId" integer NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally no-op: column may be required by the application model.
    }
}
