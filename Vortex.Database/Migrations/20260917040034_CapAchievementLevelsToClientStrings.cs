using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Seeds;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Applies <c>achievement_client_level_caps.sql</c>: the six achievements the client can only
    /// name at level 1 keep one level, and <c>FriendCount</c> takes the name the code credits.
    /// </summary>
    /// <remarks>
    /// Both seeds gave every achievement five levels, a figure taken from nothing. Level 2 of a
    /// one-level family has no string, so the achievement window showed the key itself --
    /// <c>badge_name_ACH_AvatarLooks5</c> -- under a badge image that does not exist either.
    /// </remarks>
    public partial class CapAchievementLevelsToClientStrings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SeedScripts.Read("achievement_client_level_caps.sql"));

            // achievement_resolutions.sql joins on the achievement's name, and it now spells the
            // renamed one. Its own migration ran before this rename, so on a fresh database the
            // statue is missing that challenge until the seed is applied once more; INSERT IGNORE
            // over a unique achievement_id makes the second pass a no-op everywhere else.
            migrationBuilder.Sql(SeedScripts.Read("achievement_resolutions.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The levels being dropped are the ones the client cannot render. Putting them back
            // would restore the bug, and the progress that rode on them is gone.
        }
    }
}
