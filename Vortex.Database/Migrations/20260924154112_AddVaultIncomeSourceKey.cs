using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddVaultIncomeSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AddColumn<string>(
                    name: "source_key",
                    table: "player_vault_income_rewards",
                    type: "varchar(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: ""
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "claimed_at",
                table: "player_vault_income_rewards",
                type: "datetime(6)",
                nullable: true
            );

            // Rows written before this column existed get a key of their own, so the unique index
            // below can be created over them. Nothing ever deposited into the vault before this
            // migration, so on a real hotel this touches nothing -- it is here so that is not an
            // assumption.
            migrationBuilder.Sql(
                "UPDATE player_vault_income_rewards SET source_key = CONCAT('legacy:', id) WHERE source_key = '';"
            );

            // The composite index is created before the old single-column one is dropped: MySQL
            // refuses to drop an index that backs the player_id foreign key while no other index
            // covers it.
            migrationBuilder.CreateIndex(
                name: "IX_player_vault_income_rewards_player_id_source_key",
                table: "player_vault_income_rewards",
                columns: new[] { "player_id", "source_key" },
                unique: true
            );

            migrationBuilder.DropIndex(
                name: "IX_player_vault_income_rewards_player_id",
                table: "player_vault_income_rewards"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_player_vault_income_rewards_player_id",
                table: "player_vault_income_rewards",
                column: "player_id"
            );

            migrationBuilder.DropIndex(
                name: "IX_player_vault_income_rewards_player_id_source_key",
                table: "player_vault_income_rewards"
            );

            migrationBuilder.DropColumn(name: "claimed_at", table: "player_vault_income_rewards");

            migrationBuilder.DropColumn(name: "source_key", table: "player_vault_income_rewards");
        }
    }
}
