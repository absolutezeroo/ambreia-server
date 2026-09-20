using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStepValueSides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "left_arg",
                table: "reward_track_step_filters",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "left_kind",
                table: "reward_track_step_filters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "right_arg",
                table: "reward_track_step_filters",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "right_kind",
                table: "reward_track_step_filters",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "left_arg",
                table: "reward_track_step_filters");

            migrationBuilder.DropColumn(
                name: "left_kind",
                table: "reward_track_step_filters");

            migrationBuilder.DropColumn(
                name: "right_arg",
                table: "reward_track_step_filters");

            migrationBuilder.DropColumn(
                name: "right_kind",
                table: "reward_track_step_filters");
        }
    }
}
