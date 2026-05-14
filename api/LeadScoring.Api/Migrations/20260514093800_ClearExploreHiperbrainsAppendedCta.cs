using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClearExploreHiperbrainsAppendedCta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE public."EmailTemplates"
                SET "CtaButtonText" = NULL,
                    "CtaLink" = NULL,
                    "UpdatedAt" = NOW()
                WHERE "CtaButtonText" IS NOT NULL
                  AND (
                    TRIM("CtaButtonText") ILIKE 'explore hiperbrains%'
                    OR TRIM("CtaButtonText") ILIKE 'explorehiperbrains%'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only migration; previous CTA labels are not restored.
        }
    }
}
