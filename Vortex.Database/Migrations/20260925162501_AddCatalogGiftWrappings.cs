using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogGiftWrappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "catalog_gift_wrappings",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        kind = table.Column<int>(type: "int", nullable: false),
                        furniture_name = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        art_index = table.Column<int>(type: "int", nullable: true),
                        is_default = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        sort_order = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
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
                        table.PrimaryKey("PK_catalog_gift_wrappings", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            // What was hardcoded in GiftWrapping.cs until now, so nothing changes on migration.
            // kind: 0 = parcel, 1 = box, 2 = ribbon. Box 7 is left out as the official server
            // does: it is the one index whose art the client does not ship.
            migrationBuilder.Sql(
                """
                INSERT INTO catalog_gift_wrappings (kind, furniture_name, art_index, is_default, sort_order)
                VALUES
                    (0, 'present_gen',  NULL, 1, 0),
                    (0, 'present_gen1', NULL, 0, 1),
                    (0, 'present_gen2', NULL, 0, 2),
                    (0, 'present_gen3', NULL, 0, 3),
                    (0, 'present_gen4', NULL, 0, 4),
                    (0, 'present_gen5', NULL, 0, 5),
                    (0, 'present_gen6', NULL, 0, 6),
                    (1, NULL, 0, 0, 0), (1, NULL, 1, 0, 1), (1, NULL, 2, 0, 2), (1, NULL, 3, 0, 3),
                    (1, NULL, 4, 0, 4), (1, NULL, 5, 0, 5), (1, NULL, 6, 0, 6), (1, NULL, 8, 0, 7),
                    (2, NULL, 0, 0, 0), (2, NULL, 1, 0, 1), (2, NULL, 2, 0, 2), (2, NULL, 3, 0, 3),
                    (2, NULL, 4, 0, 4), (2, NULL, 5, 0, 5), (2, NULL, 6, 0, 6), (2, NULL, 7, 0, 7),
                    (2, NULL, 8, 0, 8), (2, NULL, 9, 0, 9), (2, NULL, 10, 0, 10);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "catalog_gift_wrappings");
        }
    }
}
