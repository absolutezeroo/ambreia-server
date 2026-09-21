using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWebSocialFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "web_feed_posts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        group_id = table.Column<int>(type: "int", nullable: true),
                        parent_id = table.Column<int>(type: "int", nullable: true),
                        kind = table.Column<int>(type: "int", nullable: false),
                        body = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
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
                        table.PrimaryKey("PK_web_feed_posts", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_feed_posts_groups_group_id",
                            column: x => x.group_id,
                            principalTable: "groups",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_posts_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_posts_web_feed_posts_parent_id",
                            column: x => x.parent_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_feed_tags",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        tag = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
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
                        table.PrimaryKey("PK_web_feed_tags", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_feed_poll_choices",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        post_id = table.Column<int>(type: "int", nullable: false),
                        ordinal = table.Column<int>(type: "int", nullable: false),
                        label = table
                            .Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
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
                        table.PrimaryKey("PK_web_feed_poll_choices", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_feed_poll_choices_web_feed_posts_post_id",
                            column: x => x.post_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_feed_post_photos",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        post_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_web_feed_post_photos", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_feed_post_photos_camera_photos_photo_id",
                            column: x => x.photo_id,
                            principalTable: "camera_photos",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_post_photos_web_feed_posts_post_id",
                            column: x => x.post_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_feed_reactions",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        post_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        kind = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_web_feed_reactions", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_feed_reactions_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_reactions_web_feed_posts_post_id",
                            column: x => x.post_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_feed_post_tags",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        post_id = table.Column<int>(type: "int", nullable: false),
                        tag_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_web_feed_post_tags", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_feed_post_tags_web_feed_posts_post_id",
                            column: x => x.post_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_post_tags_web_feed_tags_tag_id",
                            column: x => x.tag_id,
                            principalTable: "web_feed_tags",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_feed_poll_votes",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        post_id = table.Column<int>(type: "int", nullable: false),
                        choice_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_web_feed_poll_votes", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_feed_poll_votes_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_poll_votes_web_feed_poll_choices_choice_id",
                            column: x => x.choice_id,
                            principalTable: "web_feed_poll_choices",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_web_feed_poll_votes_web_feed_posts_post_id",
                            column: x => x.post_id,
                            principalTable: "web_feed_posts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_poll_choices_post_id_ordinal",
                table: "web_feed_poll_choices",
                columns: new[] { "post_id", "ordinal" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_poll_votes_choice_id",
                table: "web_feed_poll_votes",
                column: "choice_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_poll_votes_player_id",
                table: "web_feed_poll_votes",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_poll_votes_post_id_player_id",
                table: "web_feed_poll_votes",
                columns: new[] { "post_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_post_photos_photo_id",
                table: "web_feed_post_photos",
                column: "photo_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_post_photos_post_id_ordinal",
                table: "web_feed_post_photos",
                columns: new[] { "post_id", "ordinal" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_post_tags_post_id_tag_id",
                table: "web_feed_post_tags",
                columns: new[] { "post_id", "tag_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_post_tags_tag_id",
                table: "web_feed_post_tags",
                column: "tag_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_posts_deleted_at_created_at",
                table: "web_feed_posts",
                columns: new[] { "deleted_at", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_posts_group_id",
                table: "web_feed_posts",
                column: "group_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_posts_parent_id_created_at",
                table: "web_feed_posts",
                columns: new[] { "parent_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_posts_player_id_created_at",
                table: "web_feed_posts",
                columns: new[] { "player_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_reactions_player_id",
                table: "web_feed_reactions",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_reactions_post_id_player_id_kind",
                table: "web_feed_reactions",
                columns: new[] { "post_id", "player_id", "kind" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_feed_tags_tag",
                table: "web_feed_tags",
                column: "tag",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "web_feed_poll_votes");

            migrationBuilder.DropTable(name: "web_feed_post_photos");

            migrationBuilder.DropTable(name: "web_feed_post_tags");

            migrationBuilder.DropTable(name: "web_feed_reactions");

            migrationBuilder.DropTable(name: "web_feed_poll_choices");

            migrationBuilder.DropTable(name: "web_feed_tags");

            migrationBuilder.DropTable(name: "web_feed_posts");
        }
    }
}
