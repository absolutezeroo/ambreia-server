using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCfhAppeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "appeal_created_at",
                table: "cfh_tickets",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "appeal_resolved_at",
                table: "cfh_tickets",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "appeal_status",
                table: "cfh_tickets",
                type: "int",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "appeal_created_at", table: "cfh_tickets");

            migrationBuilder.DropColumn(name: "appeal_resolved_at", table: "cfh_tickets");

            migrationBuilder.DropColumn(name: "appeal_status", table: "cfh_tickets");
        }
    }
}
