using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceFeeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "average_price_period_days",
                table: "marketplace_settings",
                type: "int",
                nullable: false,
                defaultValue: 7
            );

            migrationBuilder.AddColumn<int>(
                name: "half_tax_limit",
                table: "marketplace_settings",
                type: "int",
                nullable: false,
                defaultValue: 1000000000
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "average_price_period_days",
                table: "marketplace_settings"
            );

            migrationBuilder.DropColumn(name: "half_tax_limit", table: "marketplace_settings");
        }
    }
}
