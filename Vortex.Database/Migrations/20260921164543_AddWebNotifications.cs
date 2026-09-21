using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWebNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "web_notifications",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        kind = table.Column<int>(type: "int", nullable: false),
                        actor_player_id = table.Column<int>(type: "int", nullable: true),
                        target_id = table.Column<int>(type: "int", nullable: true),
                        data = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        seen_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_web_notifications", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_notifications_players_actor_player_id",
                            column: x => x.actor_player_id,
                            principalTable: "players",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_web_notifications_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_web_notifications_actor_player_id",
                table: "web_notifications",
                column: "actor_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_notifications_player_id_created_at",
                table: "web_notifications",
                columns: new[] { "player_id", "created_at" },
                descending: new bool[0]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "web_notifications");
        }
    }
}
