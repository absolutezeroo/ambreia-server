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
                    name: "rpg_content_packs",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: false),
                        added = table.Column<int>(type: "int", nullable: false),
                        changed = table.Column<int>(type: "int", nullable: false),
                        removed = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_rpg_content_packs", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "rpg_content_texts",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        language = table
                            .Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        body = table
                            .Column<string>(type: "longtext", maxLength: 512, nullable: false)
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
                        table.PrimaryKey("PK_rpg_content_texts", x => x.id);
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
                    name: "zone_versions",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        zone_id = table.Column<int>(type: "int", nullable: false),
                        version = table.Column<int>(type: "int", nullable: false),
                        schema_version = table.Column<int>(type: "int", nullable: false),
                        document = table
                            .Column<string>(type: "longtext", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        author_account_id = table.Column<int>(type: "int", nullable: false),
                        status = table.Column<int>(type: "int", nullable: false),
                        comment = table
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
                        table.PrimaryKey("PK_zone_versions", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "zones",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        zone_key = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        room_id = table.Column<int>(type: "int", nullable: false),
                        published_version = table.Column<int>(type: "int", nullable: true),
                        locked_by_account_id = table.Column<int>(type: "int", nullable: true),
                        locked_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_zones", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "account_tickets",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        account_id = table.Column<int>(type: "int", nullable: false),
                        ticket = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ip_address = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
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
                        table.PrimaryKey("PK_account_tickets", x => x.id);
                        table.ForeignKey(
                            name: "FK_account_tickets_player_accounts_account_id",
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
                        achievement_score = table.Column<int>(
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
                    name: "rpg_content",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        content_id = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        kind = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        document = table
                            .Column<string>(type: "longtext", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        hash = table
                            .Column<string>(type: "char(64)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        pack_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_rpg_content", x => x.id);
                        table.ForeignKey(
                            name: "FK_rpg_content_rpg_content_packs_pack_id",
                            column: x => x.pack_id,
                            principalTable: "rpg_content_packs",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "rpg_content_migrations",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        from_content_id = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        action = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        to_content_id = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        pack_id = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_rpg_content_migrations", x => x.id);
                        table.ForeignKey(
                            name: "FK_rpg_content_migrations_rpg_content_packs_pack_id",
                            column: x => x.pack_id,
                            principalTable: "rpg_content_packs",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "player_characters",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        class_id = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        specialization_id = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        level = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                        xp = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        last_room_id = table.Column<int>(type: "int", nullable: true),
                        current_health = table.Column<int>(type: "int", nullable: true),
                        current_mana = table.Column<int>(type: "int", nullable: true),
                        vitals_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_player_characters", x => x.id);
                        table.ForeignKey(
                            name: "FK_player_characters_players_player_id",
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
                    name: "rpg_item_grants",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        grant_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
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
                        table.PrimaryKey("PK_rpg_item_grants", x => x.id);
                        table.ForeignKey(
                            name: "FK_rpg_item_grants_players_player_id",
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
                    name: "rpg_items",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        player_id = table.Column<int>(type: "int", nullable: false),
                        definition_id = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        quantity = table.Column<int>(type: "int", nullable: false),
                        durability = table.Column<int>(type: "int", nullable: true),
                        bound = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        container = table.Column<int>(
                            type: "int",
                            nullable: false,
                            defaultValue: 0
                        ),
                        slot = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_rpg_items", x => x.id);
                        table.ForeignKey(
                            name: "FK_rpg_items_players_player_id",
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
                        table.ForeignKey(
                            name: "FK_furniture_rooms_room_id",
                            column: x => x.room_id,
                            principalTable: "rooms",
                            principalColumn: "id"
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
                name: "IX_account_tickets_account_id",
                table: "account_tickets",
                column: "account_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_account_tickets_ticket",
                table: "account_tickets",
                column: "ticket",
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
                name: "IX_player_accounts_email",
                table: "player_accounts",
                column: "email",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_player_characters_player_id",
                table: "player_characters",
                column: "player_id",
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
                name: "IX_room_models_name",
                table: "room_models",
                column: "name",
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
                name: "IX_rpg_content_content_id",
                table: "rpg_content",
                column: "content_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_content_pack_id",
                table: "rpg_content",
                column: "pack_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_content_migrations_pack_id",
                table: "rpg_content_migrations",
                column: "pack_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_content_texts_language",
                table: "rpg_content_texts",
                column: "language",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_item_grants_grant_key",
                table: "rpg_item_grants",
                column: "grant_key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_item_grants_player_id",
                table: "rpg_item_grants",
                column: "player_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_items_definition_id",
                table: "rpg_items",
                column: "definition_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_rpg_items_player_id",
                table: "rpg_items",
                column: "player_id"
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
                name: "IX_zone_versions_zone_id_version",
                table: "zone_versions",
                columns: new[] { "zone_id", "version" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_zones_room_id",
                table: "zones",
                column: "room_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_zones_zone_key",
                table: "zones",
                column: "zone_key",
                unique: true
            );

            // Reference data, in the order the squashed migrations applied it (M0, then the core cut).
            // Each file is under Vortex.Database/Seeds; SeedCatalogTests checks every embedded one is here.
            migrationBuilder.Sql(SeedScripts.Read("seed_world_owner.sql"));
            migrationBuilder.Sql(SeedScripts.Read("add_server_config.sql"));
            migrationBuilder.Sql(SeedScripts.Read("guild_furni_logic.sql"));
            migrationBuilder.Sql(SeedScripts.Read("furni_logic_bindings.sql"));
            migrationBuilder.Sql(SeedScripts.Read("furni_stuff_data_types.sql"));
            migrationBuilder.Sql(SeedScripts.Read("highscore_families.sql"));
            migrationBuilder.Sql(SeedScripts.Read("furni_logic_self_named_rebind.sql"));
            migrationBuilder.Sql(SeedScripts.Read("categorize_room_decorations.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "account_tickets");

            migrationBuilder.DropTable(name: "audit_events");

            migrationBuilder.DropTable(name: "error_groups");

            migrationBuilder.DropTable(name: "error_occurrences");

            migrationBuilder.DropTable(name: "furniture");

            migrationBuilder.DropTable(name: "item_events");

            migrationBuilder.DropTable(name: "player_account_roles");

            migrationBuilder.DropTable(name: "player_characters");

            migrationBuilder.DropTable(name: "role_permissions");

            migrationBuilder.DropTable(name: "room_entry_logs");

            migrationBuilder.DropTable(name: "room_rights");

            migrationBuilder.DropTable(name: "rpg_content");

            migrationBuilder.DropTable(name: "rpg_content_migrations");

            migrationBuilder.DropTable(name: "rpg_content_texts");

            migrationBuilder.DropTable(name: "rpg_item_grants");

            migrationBuilder.DropTable(name: "rpg_items");

            migrationBuilder.DropTable(name: "security_tickets");

            migrationBuilder.DropTable(name: "server_config");

            migrationBuilder.DropTable(name: "zone_versions");

            migrationBuilder.DropTable(name: "zones");

            migrationBuilder.DropTable(name: "furniture_definitions");

            migrationBuilder.DropTable(name: "roles");

            migrationBuilder.DropTable(name: "rooms");

            migrationBuilder.DropTable(name: "rpg_content_packs");

            migrationBuilder.DropTable(name: "players");

            migrationBuilder.DropTable(name: "room_models");

            migrationBuilder.DropTable(name: "player_accounts");
        }
    }
}
