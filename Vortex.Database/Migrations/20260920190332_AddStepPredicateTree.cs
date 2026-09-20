using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStepPredicateTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "reward_track_step_filters",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "parent_id",
                table: "reward_track_step_filters",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_reward_track_step_filters_parent_id",
                table: "reward_track_step_filters",
                column: "parent_id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reward_track_step_filters_parent_id",
                table: "reward_track_step_filters"
            );

            migrationBuilder.DropColumn(name: "kind", table: "reward_track_step_filters");

            migrationBuilder.DropColumn(name: "parent_id", table: "reward_track_step_filters");
        }
    }
}
