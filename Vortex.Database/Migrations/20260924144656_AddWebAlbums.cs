using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWebAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "bumped_at",
                table: "web_feed_posts",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder
                .CreateTable(
                    name: "web_albums",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        title = table
                            .Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        description = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        feed_post_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_web_albums", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_albums_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_albums_web_feed_posts_feed_post_id",
                            column: x => x.feed_post_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_album_photos",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        album_id = table.Column<int>(type: "int", nullable: false),
                        photo_id = table.Column<int>(type: "int", nullable: false),
                        ordinal = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_web_album_photos", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_album_photos_camera_photos_photo_id",
                            column: x => x.photo_id,
                            principalTable: "camera_photos",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_album_photos_web_albums_album_id",
                            column: x => x.album_id,
                            principalTable: "web_albums",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_web_album_photos_album_id_ordinal",
                table: "web_album_photos",
                columns: new[] { "album_id", "ordinal" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_album_photos_album_id_photo_id",
                table: "web_album_photos",
                columns: new[] { "album_id", "photo_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_album_photos_photo_id",
                table: "web_album_photos",
                column: "photo_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_albums_feed_post_id",
                table: "web_albums",
                column: "feed_post_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_albums_player_id_created_at",
                table: "web_albums",
                columns: new[] { "player_id", "created_at" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "web_album_photos");

            migrationBuilder.DropTable(name: "web_albums");

            migrationBuilder.DropColumn(name: "bumped_at", table: "web_feed_posts");
        }
    }
}
