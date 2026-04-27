using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProductConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProductConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductEventConfigJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProductConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProductConfigs_CompanyName_ProductName_ProductId",
                table: "CompanyProductConfigs",
                columns: new[] { "CompanyName", "ProductName", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProductConfigs");
        }
    }
}
