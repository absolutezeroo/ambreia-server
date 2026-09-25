using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class CatalogOfferCurrencyIsActivityPointType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_catalog_offers_currency_types_currency_type_id",
                table: "catalog_offers"
            );

            migrationBuilder.DropIndex(
                name: "IX_catalog_offers_currency_type_id",
                table: "catalog_offers"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_catalog_offers_currency_type_id",
                table: "catalog_offers",
                column: "currency_type_id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_offers_currency_types_currency_type_id",
                table: "catalog_offers",
                column: "currency_type_id",
                principalTable: "currency_types",
                principalColumn: "id"
            );
        }
    }
}
