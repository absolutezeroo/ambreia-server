using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "camera_photos",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        photo_key = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        scene_bytes = table.Column<int>(type: "int", nullable: false),
                        rejected_external_sprites = table.Column<int>(type: "int", nullable: false),
                        purchased_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        published_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        competition_submitted_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
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
                        table.PrimaryKey("PK_camera_photos", x => x.id);
                        table.ForeignKey(
                            name: "FK_camera_photos_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_camera_photos_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_camera_photos_photo_key",
                table: "camera_photos",
                column: "photo_key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_camera_photos_player_id_created_at",
                table: "camera_photos",
                columns: new[] { "player_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_camera_photos_room_id",
                table: "camera_photos",
                column: "room_id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "camera_photos");
        }
    }
}
