using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Where a Relic is: null while it is in the hotel, the chain address once it has been
    /// transferred out to a wallet. <c>NftAssetEntity.WalletAddress</c> carries the full rule,
    /// including the two queries that deliberately ignore it.
    /// </summary>
    /// <remarks>
    /// Nullable and with no backfill, which is the whole of it: every Relic that exists today is in
    /// the hotel, so null is already the right answer for all of them. Any other default would have
    /// taken the lot off the market at once.
    /// </remarks>
    public partial class AddNftAssetWalletAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AddColumn<string>(
                    name: "wallet_address",
                    table: "nft_assets",
                    type: "varchar(42)",
                    maxLength: 42,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_nft_assets_wallet_address",
                table: "nft_assets",
                column: "wallet_address"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_nft_assets_wallet_address", table: "nft_assets");

            migrationBuilder.DropColumn(name: "wallet_address", table: "nft_assets");
        }
    }
}
