using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Seeds;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Replaces the 35 hand-picked achievements with the 115 the client and the wiki agree on,
    /// each with its real level count and its real progress requirements.
    /// </summary>
    /// <remarks>
    /// <see cref="CapAchievementLevelsToClientStrings"/> took the six families the client can only
    /// name at level 1 down to one level. This is the same correction applied to every family at
    /// once, and it brings the thresholds with it: the earlier seeds invented those too, so
    /// <c>RoomEntry</c> asked for 1 room where the hotel asks for 5, and
    /// <c>AllTimeHotelPresence</c> for 1 minute where it asks for 30.
    /// </remarks>
    public partial class SeedAchievementCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SeedScripts.Read("achievement_catalogue.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rows being replaced are the invented ones. Restoring them would restore the bug.
        }
    }
}
