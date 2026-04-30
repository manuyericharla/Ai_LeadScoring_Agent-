using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class VisitorAttributionArchitectureV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Leads_LeadId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "LeadVisitorMaps");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "LeadVisitorMaps");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "LeadVisitorMaps");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "LeadVisitorMaps");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "LeadVisitorMaps");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "LeadVisitorMaps");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "LeadVisitorMaps");

            migrationBuilder.AddColumn<int>(
                name: "FirstSource",
                table: "Leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastSource",
                table: "Leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                table: "Events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Campaign",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitorId",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Visitors",
                columns: table => new
                {
                    VisitorId = table.Column<string>(type: "text", nullable: false),
                    FirstSource = table.Column<int>(type: "integer", nullable: false),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visitors", x => x.VisitorId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_VisitorId",
                table: "Events",
                column: "VisitorId");

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_FirstSource",
                table: "Visitors",
                column: "FirstSource");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Leads_LeadId",
                table: "Events",
                column: "LeadId",
                principalTable: "Leads",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Leads_LeadId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "Visitors");

            migrationBuilder.DropIndex(
                name: "IX_Events_VisitorId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "FirstSource",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LastSource",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "Campaign",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "VisitorId",
                table: "Events");

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "LeadVisitorMaps",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                table: "Events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Leads_LeadId",
                table: "Events",
                column: "LeadId",
                principalTable: "Leads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
