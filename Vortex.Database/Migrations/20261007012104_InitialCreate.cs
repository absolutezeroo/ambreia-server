using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Seeds;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "achievements",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        category = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        display_method = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_achievements", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "audit_events",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        category = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        action = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        severity = table
                            .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        result = table
                            .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        correlation_id = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        actor_player_id = table.Column<long>(type: "bigint", nullable: true),
                        target_player_id = table.Column<long>(type: "bigint", nullable: true),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        item_id = table.Column<long>(type: "bigint", nullable: true),
                        ip_hash = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        amount = table.Column<long>(type: "bigint", nullable: true),
                        quantity = table.Column<int>(type: "int", nullable: true),
                        data = table
                            .Column<string>(type: "text", maxLength: 512, nullable: true)
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
                        table.PrimaryKey("PK_audit_events", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "catalog_frontpage_items",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        position = table.Column<int>(type: "int", nullable: false),
                        item_name = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        item_promo_image = table
                            .Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        type = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        catalog_page_location = table
                            .Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        product_offer_id = table.Column<int>(type: "int", nullable: true),
                        product_code = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        expires_in_seconds = table.Column<int>(
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
                        table.PrimaryKey("PK_catalog_frontpage_items", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

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

            migrationBuilder
                .CreateTable(
                    name: "catalog_pages",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        catalog_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        parent_id = table.Column<int>(type: "int", nullable: true),
                        localization = table
                            .Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        name = table
                            .Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        icon = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        layout = table
                            .Column<string>(
                                type: "varchar(50)",
                                nullable: false,
                                defaultValue: "default_3x3"
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        image_data = table
                            .Column<string>(type: "longtext", nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        text_data = table
                            .Column<string>(type: "longtext", nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        sort_order = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        visible = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_catalog_pages", x => x.id);
                        table.ForeignKey(
                            name: "FK_catalog_pages_catalog_pages_parent_id",
                            column: x => x.parent_id,
                            principalTable: "catalog_pages",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "cfh_categories",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        display_order = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_cfh_categories", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "commerce_operations",
                    columns: table => new
                    {
                        id = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        kind = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        state = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        current_step = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        last_error = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        detail = table
                            .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        pivoted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        relay_type = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        relay_payload = table
                            .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        relayed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_commerce_operations", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "commerce_receipts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        operation_id = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        step_key = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        result = table
                            .Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_commerce_receipts", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "currency_types",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        type = table.Column<int>(type: "int", nullable: false),
                        activity_point_type = table.Column<int>(type: "int", nullable: true),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        starting_amount = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 200
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
                        table.PrimaryKey("PK_currency_types", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "daily_tasks",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        task_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        quest_type_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        is_bonus = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        image_version = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        catalog_name = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        required_repeats = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_daily_tasks", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "economy_ledger",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        player_id = table.Column<long>(type: "bigint", nullable: false),
                        currency = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        activity_point_type = table.Column<int>(type: "int", nullable: true),
                        delta = table.Column<long>(type: "bigint", nullable: false),
                        balance_after = table.Column<long>(type: "bigint", nullable: false),
                        reason = table
                            .Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        correlation_id = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ref_id = table.Column<long>(type: "bigint", nullable: true),
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
                        table.PrimaryKey("PK_economy_ledger", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "error_groups",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        fingerprint = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        source = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        operation = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        exception_type = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        message_signature = table
                            .Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        sample_message = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        first_seen_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: false
                        ),
                        last_seen_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        total_occurrences = table.Column<int>(type: "int", nullable: false),
                        last_actor_player_id = table.Column<long>(type: "bigint", nullable: true),
                        last_room_id = table.Column<int>(type: "int", nullable: true),
                        last_correlation_id = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_error_groups", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "error_occurrences",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        fingerprint = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        source = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        operation = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        exception_type = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        message = table
                            .Column<string>(type: "text", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        stack_trace = table
                            .Column<string>(type: "text", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        correlation_id = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        actor_player_id = table.Column<long>(type: "bigint", nullable: true),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        session_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        remote_ip = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_error_occurrences", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "figure_sellable_sets",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        figure_set_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_figure_sellable_sets", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "fishing_levels",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        level = table.Column<int>(type: "int", nullable: false),
                        xp_threshold = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_fishing_levels", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "fishing_player_state",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        fishing_level = table.Column<int>(type: "int", nullable: false),
                        fishing_xp = table.Column<int>(type: "int", nullable: false),
                        rod_quality = table.Column<int>(type: "int", nullable: false),
                        rod_xp = table.Column<int>(type: "int", nullable: false),
                        currency = table.Column<int>(type: "int", nullable: false),
                        currency_earned_today = table.Column<int>(type: "int", nullable: false),
                        currency_earned_on = table.Column<DateOnly>(type: "date", nullable: false),
                        total_catches = table.Column<int>(type: "int", nullable: false),
                        golden_catches = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_fishing_player_state", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "fishing_records",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        species_id = table.Column<int>(type: "int", nullable: false),
                        best_weight = table.Column<int>(type: "int", nullable: false),
                        caught_count = table.Column<int>(type: "int", nullable: false),
                        best_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                        table.PrimaryKey("PK_fishing_records", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "fishing_rod_tiers",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        quality = table.Column<int>(type: "int", nullable: false),
                        xp_threshold = table.Column<int>(type: "int", nullable: false),
                        name_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        hand_item_id = table.Column<int>(type: "int", nullable: false),
                        catch_multiplier = table.Column<int>(type: "int", nullable: false),
                        golden_multiplier = table.Column<int>(type: "int", nullable: false),
                        hook_havoc_chance = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_fishing_rod_tiers", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "fishing_species",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        zone_id = table.Column<int>(type: "int", nullable: false),
                        name_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        required_level = table.Column<int>(type: "int", nullable: false),
                        rarity_stars = table.Column<int>(type: "int", nullable: false),
                        catch_rate = table.Column<int>(type: "int", nullable: false),
                        rarity_weight = table.Column<int>(type: "int", nullable: false),
                        min_weight = table.Column<int>(type: "int", nullable: false),
                        max_weight = table.Column<int>(type: "int", nullable: false),
                        xp_reward = table.Column<int>(type: "int", nullable: false),
                        golden_xp_bonus = table.Column<int>(type: "int", nullable: false),
                        currency_reward = table.Column<int>(type: "int", nullable: false),
                        active_hours = table.Column<int>(type: "int", nullable: false),
                        active_weekdays = table.Column<int>(type: "int", nullable: false),
                        active_seasons = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_fishing_species", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "fishing_zones",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        furni_class = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        required_level = table.Column<int>(type: "int", nullable: false),
                        min_catches = table.Column<int>(type: "int", nullable: false),
                        max_catches = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_fishing_zones", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "furniture_definitions",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        sprite_id = table.Column<int>(type: "int", nullable: false),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        type = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        category = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        logic = table
                            .Column<string>(
                                type: "varchar(50)",
                                maxLength: 50,
                                nullable: false,
                                defaultValue: "none"
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        total_states = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        width = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        length = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        stack_height = table.Column<double>(
                            type: "double(10,3)",
                            nullable: false,
                            defaultValue: 0.0
                        ),
                        can_stack = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        can_walk = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        can_sit = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        can_lay = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        can_recycle = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        can_trade = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        can_group = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        can_sell = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        usage_policy = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        extra_data = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        stuff_data_type = table.Column<byte>(
                            type: "tinyint unsigned",
                            nullable: false,
                            defaultValue: (byte)0
                        ),
                        vending_ids = table
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
                        table.PrimaryKey("PK_furniture_definitions", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_badge_parts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        part_id = table.Column<int>(type: "int", nullable: false),
                        type = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        file_name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        mask_file_name = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_group_badge_parts", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_colors",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        color_id = table.Column<int>(type: "int", nullable: false),
                        color_hex = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
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
                        table.PrimaryKey("PK_group_colors", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "item_events",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        item_id = table.Column<long>(type: "bigint", nullable: false),
                        occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        event_type = table
                            .Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        actor_player_id = table.Column<long>(type: "bigint", nullable: true),
                        from_owner_id = table.Column<long>(type: "bigint", nullable: true),
                        to_owner_id = table.Column<long>(type: "bigint", nullable: true),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        correlation_id = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        data = table
                            .Column<string>(type: "text", maxLength: 512, nullable: true)
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
                        table.PrimaryKey("PK_item_events", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "loot_tables",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        code = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        name = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        variants = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        notes = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
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
                        table.PrimaryKey("PK_loot_tables", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "marketplace_settings",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        commission_percent = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        offer_duration_seconds = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 259200
                        ),
                        average_price_period_days = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 7
                        ),
                        half_tax_limit = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1000000000
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
                        table.PrimaryKey("PK_marketplace_settings", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "membership_gifts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        product_code = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        product_type = table
                            .Column<string>(type: "varchar(4)", maxLength: 4, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        furni_definition_id = table.Column<int>(type: "int", nullable: true),
                        extra_param = table
                            .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        quantity = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        is_vip = table.Column<bool>(
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
                        table.PrimaryKey("PK_membership_gifts", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "membership_offers",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        product_code = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        price_credits = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        price_activity_points = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        price_activity_point_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        is_vip = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        months = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        extra_days = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        is_giftable = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_membership_offers", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "navigator_flatcats",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        visible = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        automatic = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        automatic_category = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        global_category = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        staff_only = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        min_rank = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        order_num = table.Column<int>(
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
                        table.PrimaryKey("PK_navigator_flatcats", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "navigator_top_level_contexts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        search_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        visible = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        query_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        order_num = table.Column<int>(
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
                        table.PrimaryKey("PK_navigator_top_level_contexts", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "pet_levels",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        pet_type = table.Column<int>(type: "int", nullable: false),
                        level = table.Column<int>(type: "int", nullable: false),
                        experience_required = table.Column<int>(type: "int", nullable: false),
                        energy_cap = table.Column<int>(type: "int", nullable: false),
                        nutrition_cap = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_pet_levels", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "pet_skins",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        pet_type = table.Column<int>(type: "int", nullable: false),
                        skin_index = table.Column<int>(type: "int", nullable: false),
                        color = table.Column<int>(type: "int", nullable: false),
                        sellable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        rare = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
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
                        table.PrimaryKey("PK_pet_skins", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_accounts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        email = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        password_hash = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        totp_secret = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        safety_locked = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                        table.PrimaryKey("PK_player_accounts", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_word_filters",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        word = table
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
                        table.PrimaryKey("PK_player_word_filters", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "quests",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        campaign_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        chain_code = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        localization_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        quest_type = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        target_type = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        target_value = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        total_steps = table.Column<int>(type: "int", nullable: false),
                        reward_type = table.Column<int>(type: "int", nullable: false),
                        reward_amount = table.Column<int>(type: "int", nullable: false),
                        catalog_page_name = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        image_version = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        sort_order = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        easy = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        seasonal = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        seasonal_seconds = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        ends_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_quests", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "roles",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        key = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        name = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
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
                        table.PrimaryKey("PK_roles", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_models",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        model = table
                            .Column<string>(type: "longtext", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        door_x = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        door_y = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        door_rotation = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        custom = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
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
                        table.PrimaryKey("PK_room_models", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "sanction_presets",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        kind = table.Column<int>(type: "int", nullable: false),
                        preset_index = table.Column<int>(type: "int", nullable: false),
                        name = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        duration_seconds = table.Column<int>(type: "int", nullable: true),
                        message = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
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
                        table.PrimaryKey("PK_sanction_presets", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "server_config",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        config_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        config_value = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        description = table
                            .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
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
                        table.PrimaryKey("PK_server_config", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "shop_orders",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        public_id = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        product_code = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        kind = table.Column<int>(type: "int", nullable: false),
                        amount = table.Column<int>(type: "int", nullable: false),
                        price_minor = table.Column<int>(type: "int", nullable: false),
                        currency = table
                            .Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        state = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        provider = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        provider_reference = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        paid_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        fulfilled_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_shop_orders", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "shop_products",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        code = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        kind = table.Column<int>(type: "int", nullable: false),
                        amount = table.Column<int>(type: "int", nullable: false),
                        price_minor = table.Column<int>(type: "int", nullable: false),
                        currency = table
                            .Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        section = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        sort_order = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        icon = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        featured = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        is_active = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_shop_products", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_article_categories",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        code = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        label_json = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        sort_order = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_web_article_categories", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_languages",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        code = table
                            .Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        label = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        is_default = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_web_languages", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "word_filter_hits",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        source = table.Column<int>(type: "int", nullable: false),
                        word_id = table.Column<int>(type: "int", nullable: false),
                        action = table.Column<int>(type: "int", nullable: false),
                        original_text = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                        table.PrimaryKey("PK_word_filter_hits", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "word_filter_words",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        word = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        word_key = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        action = table.Column<int>(type: "int", nullable: false),
                        match = table.Column<int>(type: "int", nullable: false),
                        replacement = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        created_by = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
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
                        table.PrimaryKey("PK_word_filter_words", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "achievement_levels",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        achievement_id = table.Column<int>(type: "int", nullable: false),
                        level = table.Column<int>(type: "int", nullable: false),
                        badge_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        progress_requirement = table.Column<int>(type: "int", nullable: false),
                        reward_amount = table.Column<int>(type: "int", nullable: false),
                        reward_type = table.Column<int>(type: "int", nullable: false),
                        score_points = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_achievement_levels", x => x.id);
                        table.ForeignKey(
                            name: "FK_achievement_levels_achievements_achievement_id",
                            column: x => x.achievement_id,
                            principalTable: "achievements",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "catalog_offers",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        page_id = table.Column<int>(type: "int", nullable: false),
                        localization_id = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        cost_credits = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        cost_currency = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        currency_type_id = table.Column<int>(type: "int", nullable: true),
                        can_gift = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        can_bundle = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        membership_level = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        discount_percent = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        visible = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_catalog_offers", x => x.id);
                        table.ForeignKey(
                            name: "FK_catalog_offers_catalog_pages_page_id",
                            column: x => x.page_id,
                            principalTable: "catalog_pages",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "daily_task_rewards",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        task_id = table.Column<int>(type: "int", nullable: false),
                        product_item_type_id = table.Column<short>(
                            type: "smallint",
                            nullable: false,
                            defaultValue: (short)0
                        ),
                        reward_type_id = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        extra_params = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        amount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                        table.PrimaryKey("PK_daily_task_rewards", x => x.id);
                        table.ForeignKey(
                            name: "FK_daily_task_rewards_daily_tasks_task_id",
                            column: x => x.task_id,
                            principalTable: "daily_tasks",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "furniture_purchasable_clothing",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        furniture_definition_id = table.Column<int>(type: "int", nullable: false),
                        figure_set_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_furniture_purchasable_clothing", x => x.id);
                        table.ForeignKey(
                            name: "FK_furniture_purchasable_clothing_furniture_definitions_furnitu~",
                            column: x => x.furniture_definition_id,
                            principalTable: "furniture_definitions",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "loot_table_bindings",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        furniture_definition_id = table.Column<int>(type: "int", nullable: false),
                        pool_id = table.Column<int>(type: "int", nullable: false),
                        hits_required = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_loot_table_bindings", x => x.id);
                        table.ForeignKey(
                            name: "FK_loot_table_bindings_loot_tables_pool_id",
                            column: x => x.pool_id,
                            principalTable: "loot_tables",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "loot_table_entries",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        pool_id = table.Column<int>(type: "int", nullable: false),
                        variant = table
                            .Column<string>(
                                type: "varchar(32)",
                                maxLength: 32,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        product_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        furniture_definition_id = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        extra_param = table
                            .Column<string>(
                                type: "varchar(128)",
                                maxLength: 128,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        weight = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_loot_table_entries", x => x.id);
                        table.ForeignKey(
                            name: "FK_loot_table_entries_loot_tables_pool_id",
                            column: x => x.pool_id,
                            principalTable: "loot_tables",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "navigator_quick_links",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        top_level_context_id = table.Column<int>(type: "int", nullable: false),
                        search_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        filter = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        localization = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        query_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        force_closed = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        order_num = table.Column<int>(
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
                        table.PrimaryKey("PK_navigator_quick_links", x => x.id);
                        table.ForeignKey(
                            name: "FK_navigator_quick_links_navigator_top_level_contexts_top_level~",
                            column: x => x.top_level_context_id,
                            principalTable: "navigator_top_level_contexts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "account_bans",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: false),
                        date_expires = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        reason = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
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
                        table.PrimaryKey("PK_account_bans", x => x.id);
                        table.ForeignKey(
                            name: "FK_account_bans_player_accounts_account_id",
                            column: x => x.account_id,
                            principalTable: "player_accounts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_account_safety_questions",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: false),
                        question_1 = table.Column<int>(type: "int", nullable: false),
                        answer_1_hash = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        question_2 = table.Column<int>(type: "int", nullable: false),
                        answer_2_hash = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
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
                        table.PrimaryKey("PK_player_account_safety_questions", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_account_safety_questions_player_accounts_account_id",
                            column: x => x.account_id,
                            principalTable: "player_accounts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_account_trusted_locations",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: false),
                        fingerprint = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        last_seen_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                        table.PrimaryKey("PK_player_account_trusted_locations", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_account_trusted_locations_player_accounts_account_id",
                            column: x => x.account_id,
                            principalTable: "player_accounts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "players",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: true),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        motto = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        figure = table
                            .Column<string>(
                                type: "varchar(255)",
                                maxLength: 255,
                                nullable: false,
                                defaultValue: "hr-115-42.hd-195-19.ch-3030-82.lg-275-1408.fa-1201.ca-1804-64"
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        gender = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        perk_flags = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        last_login_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        achievement_score = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        favourite_group_id = table.Column<int>(type: "int", nullable: true),
                        trading_locked_until = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        muted_until = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        nux_completed_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        profile_visible = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                        table.PrimaryKey("PK_players", x => x.id);
                        table.ForeignKey(
                            name: "FK_players_player_accounts_account_id",
                            column: x => x.account_id,
                            principalTable: "player_accounts",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_account_roles",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: false),
                        role_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_player_account_roles", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_account_roles_player_accounts_account_id",
                            column: x => x.account_id,
                            principalTable: "player_accounts",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_player_account_roles_roles_role_id",
                            column: x => x.role_id,
                            principalTable: "roles",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "role_permissions",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        role_id = table.Column<int>(type: "int", nullable: false),
                        capability_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
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
                        table.PrimaryKey("PK_role_permissions", x => x.id);
                        table.ForeignKey(
                            name: "FK_role_permissions_roles_role_id",
                            column: x => x.role_id,
                            principalTable: "roles",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "cfh_topics",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        category_id = table.Column<int>(type: "int", nullable: false),
                        name = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        consequence = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        default_sanction_preset_id = table.Column<int>(type: "int", nullable: true),
                        display_order = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_cfh_topics", x => x.id);
                        table.ForeignKey(
                            name: "FK_cfh_topics_cfh_categories_category_id",
                            column: x => x.category_id,
                            principalTable: "cfh_categories",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_cfh_topics_sanction_presets_default_sanction_preset_id",
                            column: x => x.default_sanction_preset_id,
                            principalTable: "sanction_presets",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "web_articles",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        slug = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        category_id = table.Column<int>(type: "int", nullable: false),
                        status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        publish_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        pinned = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        author_name = table
                            .Column<string>(
                                type: "varchar(64)",
                                maxLength: 64,
                                nullable: false,
                                defaultValue: ""
                            )
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
                        table.PrimaryKey("PK_web_articles", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_articles_web_article_categories_category_id",
                            column: x => x.category_id,
                            principalTable: "web_article_categories",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "catalog_products",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        offer_id = table.Column<int>(type: "int", nullable: false),
                        product_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        definition_id = table.Column<int>(type: "int", nullable: true),
                        extra_param = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        quantity = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        unique_size = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        unique_remaining = table.Column<int>(
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
                        table.PrimaryKey("PK_catalog_products", x => x.id);
                        table.ForeignKey(
                            name: "FK_catalog_products_catalog_offers_offer_id",
                            column: x => x.offer_id,
                            principalTable: "catalog_offers",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_catalog_products_furniture_definitions_definition_id",
                            column: x => x.definition_id,
                            principalTable: "furniture_definitions",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "marketplace_offers",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        seller_id = table.Column<int>(type: "int", nullable: false),
                        definition_id = table.Column<int>(type: "int", nullable: false),
                        sprite_id = table.Column<int>(type: "int", nullable: false),
                        furni_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        extra_data = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        price = table.Column<int>(type: "int", nullable: false),
                        state = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        credits_owed = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                        table.PrimaryKey("PK_marketplace_offers", x => x.id);
                        table.ForeignKey(
                            name: "FK_marketplace_offers_furniture_definitions_definition_id",
                            column: x => x.definition_id,
                            principalTable: "furniture_definitions",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_marketplace_offers_players_seller_id",
                            column: x => x.seller_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "messenger_blocked",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        blocked_player_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_messenger_blocked", x => x.id);
                        table.ForeignKey(
                            name: "FK_messenger_blocked_players_blocked_player_id",
                            column: x => x.blocked_player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_messenger_blocked_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "messenger_categories",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
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
                        table.PrimaryKey("PK_messenger_categories", x => x.id);
                        table.ForeignKey(
                            name: "FK_messenger_categories_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "messenger_ignored",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        ignored_player_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_messenger_ignored", x => x.id);
                        table.ForeignKey(
                            name: "FK_messenger_ignored_players_ignored_player_id",
                            column: x => x.ignored_player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_messenger_ignored_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "messenger_messages",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        sender_id = table.Column<int>(type: "int", nullable: false),
                        receiver_id = table.Column<int>(type: "int", nullable: false),
                        message = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        habbicon_id = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        delivered = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
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
                        table.PrimaryKey("PK_messenger_messages", x => x.id);
                        table.ForeignKey(
                            name: "FK_messenger_messages_players_receiver_id",
                            column: x => x.receiver_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_messenger_messages_players_sender_id",
                            column: x => x.sender_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "messenger_requests",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        requested_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_messenger_requests", x => x.id);
                        table.ForeignKey(
                            name: "FK_messenger_requests_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_messenger_requests_players_requested_id",
                            column: x => x.requested_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_account_preferences",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        ui_volume = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 100
                        ),
                        furni_volume = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 100
                        ),
                        trax_volume = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 100
                        ),
                        free_flow_chat_disabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        room_invites_ignored = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        room_camera_follow_disabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        ui_flags = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                        chat_size_preference = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        chat_mode = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        chat_bubble_width = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        chat_scroll_speed = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        discord_settings_version = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        discord_show_habbo = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        discord_share_activity = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        discord_hide_in_hidden_rooms = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        discord_allow_joining = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
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
                        table.PrimaryKey("PK_player_account_preferences", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_account_preferences_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_achievements",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        achievement_id = table.Column<int>(type: "int", nullable: false),
                        progress = table.Column<int>(type: "int", nullable: false),
                        level = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_player_achievements", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_achievements_achievements_achievement_id",
                            column: x => x.achievement_id,
                            principalTable: "achievements",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_player_achievements_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_badges",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        badge_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        slot_id = table.Column<int>(type: "int", nullable: true, defaultValue: 0),
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
                        table.PrimaryKey("PK_player_badges", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_badges_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_clothing",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        figure_set_id = table.Column<int>(type: "int", nullable: false),
                        product_code = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
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
                        table.PrimaryKey("PK_player_clothing", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_clothing_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_currencies",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        currency_type_id = table.Column<int>(type: "int", nullable: false),
                        amount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                        table.PrimaryKey("PK_player_currencies", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_currencies_currency_types_currency_type_id",
                            column: x => x.currency_type_id,
                            principalTable: "currency_types",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_player_currencies_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_daily_tasks",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        task_id = table.Column<int>(type: "int", nullable: false),
                        assigned_on = table.Column<DateOnly>(type: "date", nullable: false),
                        repeats = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        status = table.Column<int>(type: "int", nullable: false),
                        expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        claimed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_player_daily_tasks", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_daily_tasks_daily_tasks_task_id",
                            column: x => x.task_id,
                            principalTable: "daily_tasks",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_player_daily_tasks_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_effects",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        effect_id = table.Column<int>(type: "int", nullable: false),
                        sub_type = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        total_duration = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        activated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        is_selected = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
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
                        table.PrimaryKey("PK_player_effects", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_effects_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_loot_claims",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        pool_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_player_loot_claims", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_loot_claims_loot_tables_pool_id",
                            column: x => x.pool_id,
                            principalTable: "loot_tables",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_player_loot_claims_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_membership_kickbacks",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        payday_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        credits_spent = table.Column<int>(type: "int", nullable: false),
                        total_rewarded = table.Column<int>(type: "int", nullable: false),
                        total_spent = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_player_membership_kickbacks", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_membership_kickbacks_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_memberships",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        membership_type = table.Column<int>(type: "int", nullable: false),
                        level = table.Column<int>(type: "int", nullable: false),
                        expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        total_months = table.Column<int>(type: "int", nullable: false),
                        gifts_available = table.Column<int>(type: "int", nullable: false),
                        next_gift_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        past_membership_days = table.Column<int>(type: "int", nullable: false),
                        past_vip_days = table.Column<int>(type: "int", nullable: false),
                        first_subscribed_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        last_expired_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        hc_badge_granted = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                        table.PrimaryKey("PK_player_memberships", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_memberships_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_mod_tool_preferences",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        window_x = table.Column<int>(type: "int", nullable: false),
                        window_y = table.Column<int>(type: "int", nullable: false),
                        window_width = table.Column<int>(type: "int", nullable: false),
                        window_height = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_player_mod_tool_preferences", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_mod_tool_preferences_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_navigator_preferences",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        window_x = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 427
                        ),
                        window_y = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 41
                        ),
                        window_width = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 425
                        ),
                        window_height = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 400
                        ),
                        left_pane_hidden = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        results_mode = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        home_room_id = table.Column<int>(type: "int", nullable: true),
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
                        table.PrimaryKey("PK_player_navigator_preferences", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_navigator_preferences_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_quests",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        quest_id = table.Column<int>(type: "int", nullable: false),
                        completed_steps = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        accepted = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        completed = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        accepted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        completed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_player_quests", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_quests_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_player_quests_quests_quest_id",
                            column: x => x.quest_id,
                            principalTable: "quests",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_reward_vault_entries",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        reward_category = table.Column<int>(type: "int", nullable: false),
                        reward_type = table.Column<int>(type: "int", nullable: false),
                        amount = table.Column<int>(type: "int", nullable: false),
                        product_code = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        source_key = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        claimed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_player_reward_vault_entries", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_reward_vault_entries_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_wardrobe_outfits",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        slot_id = table.Column<int>(type: "int", nullable: false),
                        figure = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        gender = table
                            .Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
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
                        table.PrimaryKey("PK_player_wardrobe_outfits", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_wardrobe_outfits_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "security_tickets",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        ticket = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ip_address = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        is_locked = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_security_tickets", x => x.id);
                        table.ForeignKey(
                            name: "FK_security_tickets_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

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

            migrationBuilder
                .CreateTable(
                    name: "web_article_translations",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        article_id = table.Column<int>(type: "int", nullable: false),
                        language_code = table
                            .Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        title = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        summary = table
                            .Column<string>(
                                type: "varchar(1024)",
                                maxLength: 1024,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        body_json = table
                            .Column<string>(type: "longtext", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        header_image = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        thumbnail = table
                            .Column<string>(
                                type: "varchar(512)",
                                maxLength: 512,
                                nullable: false,
                                defaultValue: ""
                            )
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
                        table.PrimaryKey("PK_web_article_translations", x => x.id);
                        table.ForeignKey(
                            name: "FK_web_article_translations_web_articles_article_id",
                            column: x => x.article_id,
                            principalTable: "web_articles",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "messenger_friends",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        requested_id = table.Column<int>(type: "int", nullable: false),
                        category_id = table.Column<int>(type: "int", nullable: true),
                        relation = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                        table.PrimaryKey("PK_messenger_friends", x => x.id);
                        table.ForeignKey(
                            name: "FK_messenger_friends_messenger_categories_category_id",
                            column: x => x.category_id,
                            principalTable: "messenger_categories",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_messenger_friends_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_messenger_friends_players_requested_id",
                            column: x => x.requested_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "bots",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        name = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        motto = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        figure = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        gender = table.Column<int>(type: "int", nullable: false),
                        x = table.Column<int>(type: "int", nullable: false),
                        y = table.Column<int>(type: "int", nullable: false),
                        z = table.Column<int>(type: "int", nullable: false),
                        rotation = table.Column<int>(type: "int", nullable: false),
                        skills_json = table
                            .Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
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
                        table.PrimaryKey("PK_bots", x => x.id);
                        table.ForeignKey(
                            name: "FK_bots_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "cfh_tickets",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        state = table.Column<int>(type: "int", nullable: false),
                        topic_id = table.Column<int>(type: "int", nullable: false),
                        reporter_player_id = table.Column<int>(type: "int", nullable: false),
                        reported_player_id = table.Column<int>(type: "int", nullable: true),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        message = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        evidence_json = table
                            .Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        picker_player_id = table.Column<int>(type: "int", nullable: true),
                        closed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        close_reason = table.Column<int>(type: "int", nullable: true),
                        sanctioned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        appeal_status = table.Column<int>(type: "int", nullable: false),
                        appeal_created_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        appeal_resolved_at = table.Column<DateTime>(
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
                        table.PrimaryKey("PK_cfh_tickets", x => x.id);
                        table.ForeignKey(
                            name: "FK_cfh_tickets_cfh_topics_topic_id",
                            column: x => x.topic_id,
                            principalTable: "cfh_topics",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_cfh_tickets_players_picker_player_id",
                            column: x => x.picker_player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_cfh_tickets_players_reported_player_id",
                            column: x => x.reported_player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_cfh_tickets_players_reporter_player_id",
                            column: x => x.reporter_player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "furniture",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        definition_id = table.Column<int>(type: "int", nullable: false),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        x = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        y = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        z = table.Column<double>(
                            type: "double(10,3)",
                            nullable: false,
                            defaultValue: 0.0
                        ),
                        direction = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        wall_offset = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        extra_data = table
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
                        table.PrimaryKey("PK_furniture", x => x.id);
                        table.ForeignKey(
                            name: "FK_furniture_furniture_definitions_definition_id",
                            column: x => x.definition_id,
                            principalTable: "furniture_definitions",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_furniture_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_blocked_members",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        blocked_by_player_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_group_blocked_members", x => x.id);
                        table.ForeignKey(
                            name: "FK_group_blocked_members_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_forum_posts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        thread_id = table.Column<int>(type: "int", nullable: false),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        message = table
                            .Column<string>(type: "text", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        state = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        admin_player_id = table.Column<int>(type: "int", nullable: true),
                        admin_operation_at = table.Column<DateTime>(
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
                        table.PrimaryKey("PK_group_forum_posts", x => x.id);
                        table.ForeignKey(
                            name: "FK_group_forum_posts_players_admin_player_id",
                            column: x => x.admin_player_id,
                            principalTable: "players",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_group_forum_posts_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_forum_read_markers",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        read_message_count = table.Column<int>(
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
                        table.PrimaryKey("PK_group_forum_read_markers", x => x.id);
                        table.ForeignKey(
                            name: "FK_group_forum_read_markers_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_forum_settings",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        read_permission = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        post_permission = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        thread_permission = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        mod_permission = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        view_count = table.Column<int>(
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
                        table.PrimaryKey("PK_group_forum_settings", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_forum_threads",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        subject = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        state = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        is_pinned = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        post_count = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        last_post_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        last_post_player_id = table.Column<int>(type: "int", nullable: true),
                        admin_player_id = table.Column<int>(type: "int", nullable: true),
                        admin_operation_at = table.Column<DateTime>(
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
                        table.PrimaryKey("PK_group_forum_threads", x => x.id);
                        table.ForeignKey(
                            name: "FK_group_forum_threads_players_admin_player_id",
                            column: x => x.admin_player_id,
                            principalTable: "players",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_group_forum_threads_players_last_post_player_id",
                            column: x => x.last_post_player_id,
                            principalTable: "players",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_group_forum_threads_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_members",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        group_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        rank = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                        table.PrimaryKey("PK_group_members", x => x.id);
                        table.ForeignKey(
                            name: "FK_group_members_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "group_membership_requests",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        group_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_group_membership_requests", x => x.id);
                        table.ForeignKey(
                            name: "FK_group_membership_requests_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "groups",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        description = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        badge = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        type = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        color_one = table
                            .Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        color_two = table
                            .Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        admin_only_decoration = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
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
                        table.PrimaryKey("PK_groups", x => x.id);
                        table.ForeignKey(
                            name: "FK_groups_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "rooms",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        name = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        description = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        door_mode = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        password = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        model_id = table.Column<int>(type: "int", nullable: false),
                        category_id = table.Column<int>(type: "int", nullable: true),
                        group_id = table.Column<int>(type: "int", nullable: true),
                        users_now = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        players_max = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 25
                        ),
                        paint_wall = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        paint_floor = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        paint_landscape = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        wall_height = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: -1
                        ),
                        hide_walls = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        thickness_wall = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        thickness_floor = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        allow_blocking = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        allow_pets = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        allow_pets_eat = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        leave_on_door_tile = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        idle_sleep_enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: true
                        ),
                        idle_sleep_timeout_seconds = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 300
                        ),
                        idle_autokick_enabled = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        idle_autokick_timeout_seconds = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1800
                        ),
                        mute_all_pets = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        trade_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        mute_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        kick_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        ban_type = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        chat_mode_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        chat_bubble_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        chat_speed_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 1
                        ),
                        chat_flood_type = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 2
                        ),
                        chat_distance = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 50
                        ),
                        last_active = table
                            .Column<DateTime>(type: "datetime(6)", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        tag_1 = table
                            .Column<string>(type: "varchar(25)", maxLength: 25, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        tag_2 = table
                            .Column<string>(type: "varchar(25)", maxLength: 25, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        score = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        ranking = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                        table.PrimaryKey("PK_rooms", x => x.id);
                        table.ForeignKey(
                            name: "FK_rooms_groups_group_id",
                            column: x => x.group_id,
                            principalTable: "groups",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_rooms_navigator_flatcats_category_id",
                            column: x => x.category_id,
                            principalTable: "navigator_flatcats",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_rooms_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_rooms_room_models_model_id",
                            column: x => x.model_id,
                            principalTable: "room_models",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "pets",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        room_id = table.Column<int>(type: "int", nullable: true),
                        name = table
                            .Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        type = table.Column<int>(type: "int", nullable: false),
                        skin = table.Column<int>(type: "int", nullable: false),
                        color = table
                            .Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        gender = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        level = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        experience = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        has_saddle = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        saddle_definition_id = table.Column<int>(type: "int", nullable: true),
                        riding_permission = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false,
                            defaultValue: false
                        ),
                        x = table.Column<int>(type: "int", nullable: false),
                        y = table.Column<int>(type: "int", nullable: false),
                        z = table.Column<double>(type: "double(10,3)", nullable: false),
                        direction = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_pets", x => x.id);
                        table.ForeignKey(
                            name: "FK_pets_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_pets_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id"
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_bans",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        date_expires = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                        table.PrimaryKey("PK_room_bans", x => x.id);
                        table.ForeignKey(
                            name: "FK_room_bans_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_room_bans_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_chatlogs",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        target_player_id = table.Column<int>(type: "int", nullable: true),
                        message = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
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
                        table.PrimaryKey("PK_room_chatlogs", x => x.id);
                        table.ForeignKey(
                            name: "FK_room_chatlogs_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_room_chatlogs_players_target_player_id",
                            column: x => x.target_player_id,
                            principalTable: "players",
                            principalColumn: "id"
                        );
                        table.ForeignKey(
                            name: "FK_room_chatlogs_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_entry_logs",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        exited_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_room_entry_logs", x => x.id);
                        table.ForeignKey(
                            name: "FK_room_entry_logs_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_room_entry_logs_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_filter_words",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        word = table
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
                        table.PrimaryKey("PK_room_filter_words", x => x.id);
                        table.ForeignKey(
                            name: "FK_room_filter_words_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_mutes",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        date_expires = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                        table.PrimaryKey("PK_room_mutes", x => x.id);
                        table.ForeignKey(
                            name: "FK_room_mutes_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_room_mutes_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "room_rights",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        room_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_room_rights", x => x.id);
                        table.ForeignKey(
                            name: "FK_room_rights_players_player_id",
                            column: x => x.player_id,
                            principalTable: "players",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_room_rights_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_account_bans_account_id",
                table: "account_bans",
                column: "account_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_achievement_levels_achievement_id_level",
                table: "achievement_levels",
                columns: new[] { "achievement_id", "level" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_achievements_name",
                table: "achievements",
                column: "name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_actor_player_id_occurred_at",
                table: "audit_events",
                columns: new[] { "actor_player_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_category_occurred_at",
                table: "audit_events",
                columns: new[] { "category", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_correlation_id",
                table: "audit_events",
                column: "correlation_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ip_hash",
                table: "audit_events",
                column: "ip_hash"
            );

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_room_id_occurred_at",
                table: "audit_events",
                columns: new[] { "room_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_target_player_id_occurred_at",
                table: "audit_events",
                columns: new[] { "target_player_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bots_player_id",
                table: "bots",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(name: "IX_bots_room_id", table: "bots", column: "room_id");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_offers_page_id",
                table: "catalog_offers",
                column: "page_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_catalog_pages_parent_id",
                table: "catalog_pages",
                column: "parent_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_definition_id",
                table: "catalog_products",
                column: "definition_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_offer_id",
                table: "catalog_products",
                column: "offer_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_tickets_picker_player_id",
                table: "cfh_tickets",
                column: "picker_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_tickets_reported_player_id",
                table: "cfh_tickets",
                column: "reported_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_tickets_reporter_player_id",
                table: "cfh_tickets",
                column: "reporter_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_tickets_room_id",
                table: "cfh_tickets",
                column: "room_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_tickets_state",
                table: "cfh_tickets",
                column: "state"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_tickets_topic_id",
                table: "cfh_tickets",
                column: "topic_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_topics_category_id",
                table: "cfh_topics",
                column: "category_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_cfh_topics_default_sanction_preset_id",
                table: "cfh_topics",
                column: "default_sanction_preset_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_commerce_operations_player_id",
                table: "commerce_operations",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_commerce_operations_relayed_at",
                table: "commerce_operations",
                column: "relayed_at"
            );

            migrationBuilder.CreateIndex(
                name: "IX_commerce_operations_state_pivoted_at",
                table: "commerce_operations",
                columns: new[] { "state", "pivoted_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_commerce_operations_state_updated_at",
                table: "commerce_operations",
                columns: new[] { "state", "updated_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_commerce_receipts_operation_id_step_key",
                table: "commerce_receipts",
                columns: new[] { "operation_id", "step_key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_daily_task_rewards_task_id",
                table: "daily_task_rewards",
                column: "task_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_daily_tasks_task_code",
                table: "daily_tasks",
                column: "task_code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_economy_ledger_correlation_id",
                table: "economy_ledger",
                column: "correlation_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_economy_ledger_player_id_occurred_at",
                table: "economy_ledger",
                columns: new[] { "player_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_groups_fingerprint",
                table: "error_groups",
                column: "fingerprint",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_groups_last_seen_at",
                table: "error_groups",
                column: "last_seen_at"
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_groups_source_operation_exception_type",
                table: "error_groups",
                columns: new[] { "source", "operation", "exception_type" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_occurrences_fingerprint",
                table: "error_occurrences",
                column: "fingerprint"
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_occurrences_group_id",
                table: "error_occurrences",
                column: "group_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_occurrences_occurred_at",
                table: "error_occurrences",
                column: "occurred_at"
            );

            migrationBuilder.CreateIndex(
                name: "IX_error_occurrences_source_occurred_at",
                table: "error_occurrences",
                columns: new[] { "source", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_figure_sellable_sets_figure_set_id",
                table: "figure_sellable_sets",
                column: "figure_set_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_levels_level",
                table: "fishing_levels",
                column: "level",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_player_state_player_id",
                table: "fishing_player_state",
                column: "player_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_records_player_id_species_id",
                table: "fishing_records",
                columns: new[] { "player_id", "species_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_records_species_id_best_weight",
                table: "fishing_records",
                columns: new[] { "species_id", "best_weight" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_rod_tiers_quality",
                table: "fishing_rod_tiers",
                column: "quality",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_species_zone_id",
                table: "fishing_species",
                column: "zone_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_fishing_zones_furni_class",
                table: "fishing_zones",
                column: "furni_class",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_furniture_definition_id",
                table: "furniture",
                column: "definition_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_furniture_player_id_room_id_deleted_at",
                table: "furniture",
                columns: new[] { "player_id", "room_id", "deleted_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_furniture_room_id",
                table: "furniture",
                column: "room_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_furniture_definitions_sprite_id_type_category",
                table: "furniture_definitions",
                columns: new[] { "sprite_id", "type", "category" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_furniture_purchasable_clothing_definition_set",
                table: "furniture_purchasable_clothing",
                columns: new[] { "furniture_definition_id", "figure_set_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_badge_parts_part_id_type",
                table: "group_badge_parts",
                columns: new[] { "part_id", "type" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_blocked_members_group_id_player_id",
                table: "group_blocked_members",
                columns: new[] { "group_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_blocked_members_player_id",
                table: "group_blocked_members",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_colors_color_id",
                table: "group_colors",
                column: "color_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_posts_admin_player_id",
                table: "group_forum_posts",
                column: "admin_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_posts_group_id",
                table: "group_forum_posts",
                column: "group_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_posts_player_id",
                table: "group_forum_posts",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_posts_thread_id",
                table: "group_forum_posts",
                column: "thread_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_read_markers_group_id_player_id",
                table: "group_forum_read_markers",
                columns: new[] { "group_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_read_markers_player_id",
                table: "group_forum_read_markers",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_settings_group_id",
                table: "group_forum_settings",
                column: "group_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_threads_admin_player_id",
                table: "group_forum_threads",
                column: "admin_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_threads_group_id",
                table: "group_forum_threads",
                column: "group_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_threads_last_post_at",
                table: "group_forum_threads",
                column: "last_post_at"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_threads_last_post_player_id",
                table: "group_forum_threads",
                column: "last_post_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_forum_threads_player_id",
                table: "group_forum_threads",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_members_group_id_player_id",
                table: "group_members",
                columns: new[] { "group_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_members_player_id",
                table: "group_members",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_membership_requests_group_id_player_id",
                table: "group_membership_requests",
                columns: new[] { "group_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_group_membership_requests_player_id",
                table: "group_membership_requests",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_groups_player_id",
                table: "groups",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_groups_room_id",
                table: "groups",
                column: "room_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_item_events_correlation_id",
                table: "item_events",
                column: "correlation_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_item_events_item_id_occurred_at",
                table: "item_events",
                columns: new[] { "item_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_loot_table_bindings_furniture_definition_id",
                table: "loot_table_bindings",
                column: "furniture_definition_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_loot_table_bindings_pool_id",
                table: "loot_table_bindings",
                column: "pool_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_loot_table_entries_pool_id_enabled",
                table: "loot_table_entries",
                columns: new[] { "pool_id", "enabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_loot_tables_code",
                table: "loot_tables",
                column: "code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_offers_definition_id",
                table: "marketplace_offers",
                column: "definition_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_offers_seller_id_state",
                table: "marketplace_offers",
                columns: new[] { "seller_id", "state" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_offers_state_expires_at",
                table: "marketplace_offers",
                columns: new[] { "state", "expires_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_blocked_blocked_player_id",
                table: "messenger_blocked",
                column: "blocked_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_blocked_player_id_blocked_player_id",
                table: "messenger_blocked",
                columns: new[] { "player_id", "blocked_player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_categories_player_id_name",
                table: "messenger_categories",
                columns: new[] { "player_id", "name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_friends_category_id",
                table: "messenger_friends",
                column: "category_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_friends_player_id_requested_id",
                table: "messenger_friends",
                columns: new[] { "player_id", "requested_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_friends_requested_id",
                table: "messenger_friends",
                column: "requested_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_ignored_ignored_player_id",
                table: "messenger_ignored",
                column: "ignored_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_ignored_player_id_ignored_player_id",
                table: "messenger_ignored",
                columns: new[] { "player_id", "ignored_player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_messages_receiver_id_sender_id_timestamp",
                table: "messenger_messages",
                columns: new[] { "receiver_id", "sender_id", "timestamp" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_messages_sender_id_receiver_id_timestamp",
                table: "messenger_messages",
                columns: new[] { "sender_id", "receiver_id", "timestamp" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_requests_player_id_requested_id",
                table: "messenger_requests",
                columns: new[] { "player_id", "requested_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_messenger_requests_requested_id",
                table: "messenger_requests",
                column: "requested_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_navigator_quick_links_top_level_context_id",
                table: "navigator_quick_links",
                column: "top_level_context_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_navigator_top_level_contexts_search_code",
                table: "navigator_top_level_contexts",
                column: "search_code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_pet_levels_pet_type_level",
                table: "pet_levels",
                columns: new[] { "pet_type", "level" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_pet_skins_pet_type",
                table: "pet_skins",
                column: "pet_type"
            );

            migrationBuilder.CreateIndex(
                name: "IX_pet_skins_pet_type_skin_index_rare",
                table: "pet_skins",
                columns: new[] { "pet_type", "skin_index", "rare" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_pets_player_id",
                table: "pets",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(name: "IX_pets_room_id", table: "pets", column: "room_id");

            migrationBuilder.CreateIndex(
                name: "IX_player_account_preferences_player_id",
                table: "player_account_preferences",
                column: "player_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_account_roles_account_id_role_id",
                table: "player_account_roles",
                columns: new[] { "account_id", "role_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_account_roles_role_id",
                table: "player_account_roles",
                column: "role_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_account_safety_questions_account_id",
                table: "player_account_safety_questions",
                column: "account_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_account_trusted_locations_account_id_fingerprint",
                table: "player_account_trusted_locations",
                columns: new[] { "account_id", "fingerprint" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_accounts_email",
                table: "player_accounts",
                column: "email",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_achievements_achievement_id",
                table: "player_achievements",
                column: "achievement_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_achievements_player_id_achievement_id",
                table: "player_achievements",
                columns: new[] { "player_id", "achievement_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_badges_player_id_badge_code",
                table: "player_badges",
                columns: new[] { "player_id", "badge_code" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_clothing_player_set_product",
                table: "player_clothing",
                columns: new[] { "player_id", "figure_set_id", "product_code" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_currencies_currency_type_id",
                table: "player_currencies",
                column: "currency_type_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_currencies_player_id_currency_type_id",
                table: "player_currencies",
                columns: new[] { "player_id", "currency_type_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_daily_tasks_player_id_assigned_on",
                table: "player_daily_tasks",
                columns: new[] { "player_id", "assigned_on" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_daily_tasks_player_id_task_id_assigned_on",
                table: "player_daily_tasks",
                columns: new[] { "player_id", "task_id", "assigned_on" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_daily_tasks_task_id",
                table: "player_daily_tasks",
                column: "task_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_effects_player_id",
                table: "player_effects",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_loot_claims_player_id_pool_id",
                table: "player_loot_claims",
                columns: new[] { "player_id", "pool_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_loot_claims_pool_id",
                table: "player_loot_claims",
                column: "pool_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_membership_kickbacks_player_id",
                table: "player_membership_kickbacks",
                column: "player_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_memberships_player_id_membership_type",
                table: "player_memberships",
                columns: new[] { "player_id", "membership_type" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_mod_tool_preferences_player_id",
                table: "player_mod_tool_preferences",
                column: "player_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_navigator_preferences_player_id",
                table: "player_navigator_preferences",
                column: "player_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_quests_player_id_quest_id",
                table: "player_quests",
                columns: new[] { "player_id", "quest_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_quests_quest_id",
                table: "player_quests",
                column: "quest_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_reward_vault_entries_player_id_source_key",
                table: "player_reward_vault_entries",
                columns: new[] { "player_id", "source_key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_wardrobe_outfits_player_id_slot_id",
                table: "player_wardrobe_outfits",
                columns: new[] { "player_id", "slot_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_word_filters_player_id_word",
                table: "player_word_filters",
                columns: new[] { "player_id", "word" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_players_account_id",
                table: "players",
                column: "account_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_players_name",
                table: "players",
                column: "name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_quests_campaign_code",
                table: "quests",
                column: "campaign_code"
            );

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_role_id_capability_key",
                table: "role_permissions",
                columns: new[] { "role_id", "capability_key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_roles_key",
                table: "roles",
                column: "key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_bans_player_id",
                table: "room_bans",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_bans_room_id_player_id",
                table: "room_bans",
                columns: new[] { "room_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_chatlogs_player_id_created_at",
                table: "room_chatlogs",
                columns: new[] { "player_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_chatlogs_room_id_created_at",
                table: "room_chatlogs",
                columns: new[] { "room_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_chatlogs_target_player_id",
                table: "room_chatlogs",
                column: "target_player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_entry_logs_player_id_room_id_exited_at",
                table: "room_entry_logs",
                columns: new[] { "player_id", "room_id", "exited_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_entry_logs_room_id",
                table: "room_entry_logs",
                column: "room_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_filter_words_room_id_word",
                table: "room_filter_words",
                columns: new[] { "room_id", "word" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_models_name",
                table: "room_models",
                column: "name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_mutes_player_id",
                table: "room_mutes",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_mutes_room_id_player_id",
                table: "room_mutes",
                columns: new[] { "room_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_rights_player_id",
                table: "room_rights",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_room_rights_room_id_player_id",
                table: "room_rights",
                columns: new[] { "room_id", "player_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_rooms_category_id",
                table: "rooms",
                column: "category_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_rooms_group_id",
                table: "rooms",
                column: "group_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_rooms_model_id",
                table: "rooms",
                column: "model_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_rooms_player_id",
                table: "rooms",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_sanction_presets_kind_preset_index",
                table: "sanction_presets",
                columns: new[] { "kind", "preset_index" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_security_tickets_player_id",
                table: "security_tickets",
                column: "player_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_security_tickets_ticket",
                table: "security_tickets",
                column: "ticket",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_server_config_config_key",
                table: "server_config",
                column: "config_key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shop_orders_player_id_id",
                table: "shop_orders",
                columns: new[] { "player_id", "id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_shop_orders_provider_provider_reference",
                table: "shop_orders",
                columns: new[] { "provider", "provider_reference" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shop_orders_public_id",
                table: "shop_orders",
                column: "public_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shop_orders_state",
                table: "shop_orders",
                column: "state"
            );

            migrationBuilder.CreateIndex(
                name: "IX_shop_products_code",
                table: "shop_products",
                column: "code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shop_products_is_active_section_sort_order",
                table: "shop_products",
                columns: new[] { "is_active", "section", "sort_order" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_article_categories_code",
                table: "web_article_categories",
                column: "code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_article_translations_article_id_language_code",
                table: "web_article_translations",
                columns: new[] { "article_id", "language_code" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_articles_category_id",
                table: "web_articles",
                column: "category_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_articles_slug",
                table: "web_articles",
                column: "slug",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_articles_status_publish_at",
                table: "web_articles",
                columns: new[] { "status", "publish_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_web_languages_code",
                table: "web_languages",
                column: "code",
                unique: true
            );

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

            migrationBuilder.CreateIndex(
                name: "IX_word_filter_hits_occurred_at",
                table: "word_filter_hits",
                column: "occurred_at"
            );

            migrationBuilder.CreateIndex(
                name: "IX_word_filter_hits_player_id_occurred_at",
                table: "word_filter_hits",
                columns: new[] { "player_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_word_filter_words_word_key",
                table: "word_filter_words",
                column: "word_key",
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_bots_rooms_room_id",
                table: "bots",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_cfh_tickets_rooms_room_id",
                table: "cfh_tickets",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_furniture_rooms_room_id",
                table: "furniture",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_blocked_members_groups_group_id",
                table: "group_blocked_members",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_forum_posts_group_forum_threads_thread_id",
                table: "group_forum_posts",
                column: "thread_id",
                principalTable: "group_forum_threads",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_forum_posts_groups_group_id",
                table: "group_forum_posts",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_forum_read_markers_groups_group_id",
                table: "group_forum_read_markers",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_forum_settings_groups_group_id",
                table: "group_forum_settings",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_forum_threads_groups_group_id",
                table: "group_forum_threads",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_members_groups_group_id",
                table: "group_members",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_group_membership_requests_groups_group_id",
                table: "group_membership_requests",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_groups_rooms_room_id",
                table: "groups",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict
            );

            // Reference data, in the order the squashed migrations applied it (M0). Each file is
            // under Vortex.Database/Seeds and SeedCatalogTests checks every embedded one is here.
            migrationBuilder.Sql(SeedScripts.Read("seed_dashboard_staff_actor.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_marketplace_settings.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_achievements.sql"));
            migrationBuilder.Sql(SeedScripts.Read("seed_more_achievements_fix_categories.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_quests.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_daily_quests.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_seasonal_quest.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_server_config.sql"));
            migrationBuilder.Sql(SeedScripts.Read("group_badge_parts.sql"));
            migrationBuilder.Sql(SeedScripts.Read("group_colors.sql"));
            migrationBuilder.Sql(SeedScripts.Read("catalog_pet_products.sql"));
            migrationBuilder.Sql(SeedScripts.Read("guild_furni_logic.sql"));
            migrationBuilder.Sql(SeedScripts.Read("furni_logic_bindings.sql"));
            migrationBuilder.Sql(SeedScripts.Read("furni_stuff_data_types.sql"));
            migrationBuilder.Sql(SeedScripts.Read("highscore_families.sql"));
            migrationBuilder.Sql(SeedScripts.Read("navigator_configuration.sql"));
            migrationBuilder.Sql(SeedScripts.Read("currencies.sql"));
            migrationBuilder.Sql(SeedScripts.Read("purchasable_clothing.sql"));
            migrationBuilder.Sql(SeedScripts.Read("fishing.sql"));
            migrationBuilder.Sql(SeedScripts.Read("furni_logic_self_named_rebind.sql"));
            migrationBuilder.Sql(SeedScripts.Read("achievement_client_level_caps.sql"));
            migrationBuilder.Sql(SeedScripts.Read("achievement_catalogue.sql"));
            migrationBuilder.Sql(SeedScripts.Read("pet_levels.sql"));
            migrationBuilder.Sql(SeedScripts.Read("pet_skins.sql"));
            migrationBuilder.Sql(SeedScripts.Read("categorize_room_decorations.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_catalog_gift_wrappings.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_players_player_accounts_account_id",
                table: "players"
            );

            migrationBuilder.DropForeignKey(name: "FK_groups_players_player_id", table: "groups");

            migrationBuilder.DropForeignKey(name: "FK_rooms_players_player_id", table: "rooms");

            migrationBuilder.DropForeignKey(name: "FK_groups_rooms_room_id", table: "groups");

            migrationBuilder.DropTable(name: "account_bans");

            migrationBuilder.DropTable(name: "achievement_levels");

            migrationBuilder.DropTable(name: "audit_events");

            migrationBuilder.DropTable(name: "bots");

            migrationBuilder.DropTable(name: "catalog_frontpage_items");

            migrationBuilder.DropTable(name: "catalog_gift_wrappings");

            migrationBuilder.DropTable(name: "catalog_products");

            migrationBuilder.DropTable(name: "cfh_tickets");

            migrationBuilder.DropTable(name: "commerce_operations");

            migrationBuilder.DropTable(name: "commerce_receipts");

            migrationBuilder.DropTable(name: "daily_task_rewards");

            migrationBuilder.DropTable(name: "economy_ledger");

            migrationBuilder.DropTable(name: "error_groups");

            migrationBuilder.DropTable(name: "error_occurrences");

            migrationBuilder.DropTable(name: "figure_sellable_sets");

            migrationBuilder.DropTable(name: "fishing_levels");

            migrationBuilder.DropTable(name: "fishing_player_state");

            migrationBuilder.DropTable(name: "fishing_records");

            migrationBuilder.DropTable(name: "fishing_rod_tiers");

            migrationBuilder.DropTable(name: "fishing_species");

            migrationBuilder.DropTable(name: "fishing_zones");

            migrationBuilder.DropTable(name: "furniture");

            migrationBuilder.DropTable(name: "furniture_purchasable_clothing");

            migrationBuilder.DropTable(name: "group_badge_parts");

            migrationBuilder.DropTable(name: "group_blocked_members");

            migrationBuilder.DropTable(name: "group_colors");

            migrationBuilder.DropTable(name: "group_forum_posts");

            migrationBuilder.DropTable(name: "group_forum_read_markers");

            migrationBuilder.DropTable(name: "group_forum_settings");

            migrationBuilder.DropTable(name: "group_members");

            migrationBuilder.DropTable(name: "group_membership_requests");

            migrationBuilder.DropTable(name: "item_events");

            migrationBuilder.DropTable(name: "loot_table_bindings");

            migrationBuilder.DropTable(name: "loot_table_entries");

            migrationBuilder.DropTable(name: "marketplace_offers");

            migrationBuilder.DropTable(name: "marketplace_settings");

            migrationBuilder.DropTable(name: "membership_gifts");

            migrationBuilder.DropTable(name: "membership_offers");

            migrationBuilder.DropTable(name: "messenger_blocked");

            migrationBuilder.DropTable(name: "messenger_friends");

            migrationBuilder.DropTable(name: "messenger_ignored");

            migrationBuilder.DropTable(name: "messenger_messages");

            migrationBuilder.DropTable(name: "messenger_requests");

            migrationBuilder.DropTable(name: "navigator_quick_links");

            migrationBuilder.DropTable(name: "pet_levels");

            migrationBuilder.DropTable(name: "pet_skins");

            migrationBuilder.DropTable(name: "pets");

            migrationBuilder.DropTable(name: "player_account_preferences");

            migrationBuilder.DropTable(name: "player_account_roles");

            migrationBuilder.DropTable(name: "player_account_safety_questions");

            migrationBuilder.DropTable(name: "player_account_trusted_locations");

            migrationBuilder.DropTable(name: "player_achievements");

            migrationBuilder.DropTable(name: "player_badges");

            migrationBuilder.DropTable(name: "player_clothing");

            migrationBuilder.DropTable(name: "player_currencies");

            migrationBuilder.DropTable(name: "player_daily_tasks");

            migrationBuilder.DropTable(name: "player_effects");

            migrationBuilder.DropTable(name: "player_loot_claims");

            migrationBuilder.DropTable(name: "player_membership_kickbacks");

            migrationBuilder.DropTable(name: "player_memberships");

            migrationBuilder.DropTable(name: "player_mod_tool_preferences");

            migrationBuilder.DropTable(name: "player_navigator_preferences");

            migrationBuilder.DropTable(name: "player_quests");

            migrationBuilder.DropTable(name: "player_reward_vault_entries");

            migrationBuilder.DropTable(name: "player_wardrobe_outfits");

            migrationBuilder.DropTable(name: "player_word_filters");

            migrationBuilder.DropTable(name: "role_permissions");

            migrationBuilder.DropTable(name: "room_bans");

            migrationBuilder.DropTable(name: "room_chatlogs");

            migrationBuilder.DropTable(name: "room_entry_logs");

            migrationBuilder.DropTable(name: "room_filter_words");

            migrationBuilder.DropTable(name: "room_mutes");

            migrationBuilder.DropTable(name: "room_rights");

            migrationBuilder.DropTable(name: "security_tickets");

            migrationBuilder.DropTable(name: "server_config");

            migrationBuilder.DropTable(name: "shop_orders");

            migrationBuilder.DropTable(name: "shop_products");

            migrationBuilder.DropTable(name: "web_article_translations");

            migrationBuilder.DropTable(name: "web_languages");

            migrationBuilder.DropTable(name: "web_notifications");

            migrationBuilder.DropTable(name: "word_filter_hits");

            migrationBuilder.DropTable(name: "word_filter_words");

            migrationBuilder.DropTable(name: "catalog_offers");

            migrationBuilder.DropTable(name: "cfh_topics");

            migrationBuilder.DropTable(name: "group_forum_threads");

            migrationBuilder.DropTable(name: "furniture_definitions");

            migrationBuilder.DropTable(name: "messenger_categories");

            migrationBuilder.DropTable(name: "navigator_top_level_contexts");

            migrationBuilder.DropTable(name: "achievements");

            migrationBuilder.DropTable(name: "currency_types");

            migrationBuilder.DropTable(name: "daily_tasks");

            migrationBuilder.DropTable(name: "loot_tables");

            migrationBuilder.DropTable(name: "quests");

            migrationBuilder.DropTable(name: "roles");

            migrationBuilder.DropTable(name: "web_articles");

            migrationBuilder.DropTable(name: "catalog_pages");

            migrationBuilder.DropTable(name: "cfh_categories");

            migrationBuilder.DropTable(name: "sanction_presets");

            migrationBuilder.DropTable(name: "web_article_categories");

            migrationBuilder.DropTable(name: "player_accounts");

            migrationBuilder.DropTable(name: "players");

            migrationBuilder.DropTable(name: "rooms");

            migrationBuilder.DropTable(name: "groups");

            migrationBuilder.DropTable(name: "navigator_flatcats");

            migrationBuilder.DropTable(name: "room_models");
        }
    }
}
