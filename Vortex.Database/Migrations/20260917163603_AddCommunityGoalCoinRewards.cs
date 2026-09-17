using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityGoalCoinRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rewarded_level",
                table: "community_goals",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "reward_coins",
                table: "community_goal_levels",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder
                .CreateTable(
                    name: "community_goal_payouts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        goal_id = table.Column<int>(type: "int", nullable: false),
                        level_number = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        contributor_rank = table.Column<int>(type: "int", nullable: false),
                        score = table.Column<int>(type: "int", nullable: false),
                        address = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        amount_coins = table.Column<int>(type: "int", nullable: false),
                        minted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        tx_hash = table
                            .Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        last_error = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        created_at = table
                            .Column<DateTime>(type: "datetime(6)", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        updated_at = table
                            .Column<DateTime>(type: "datetime(6)", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.ComputedColumn
                            ),
                        deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_community_goal_payouts", x => x.id);
                        table.ForeignKey(
                            name: "FK_community_goal_payouts_community_goals_goal_id",
                            column: x => x.goal_id,
                            principalTable: "community_goals",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_community_goal_payouts_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_community_goal_payouts_goal_id_level_number_player_id",
                table: "community_goal_payouts",
                columns: new[] { "goal_id", "level_number", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_goal_payouts_minted_at",
                table: "community_goal_payouts",
                column: "minted_at"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_goal_payouts_player_id",
                table: "community_goal_payouts",
                column: "player_id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "community_goal_payouts");

            migrationBuilder.DropColumn(name: "rewarded_level", table: "community_goals");

            migrationBuilder.DropColumn(name: "reward_coins", table: "community_goal_levels");
        }
    }
}
