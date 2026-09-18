using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropPerPetRespectCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "respect_last_reset_date", table: "pets");

            migrationBuilder.DropColumn(name: "respect_today_count", table: "pets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "respect_last_reset_date",
                table: "pets",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "respect_today_count",
                table: "pets",
                type: "int",
                nullable: false,
                defaultValue: 0
            );
        }
    }
}
