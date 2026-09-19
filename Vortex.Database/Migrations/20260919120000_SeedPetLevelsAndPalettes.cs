using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Context;
using Vortex.Database.Seeds;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Applies the two pet reference tables. <c>pet_levels.sql</c> and <c>pet_palettes.sql</c> were
    /// the only seed scripts under <c>Vortex.Database/Seeds</c> that no migration ever read, so both
    /// tables shipped empty: with no level rows a pet resolves to level 1 on any amount of
    /// experience and never levels up, and with no palette rows the catalogue offers no colour for
    /// any pet. Same defect the guild badge palettes had.
    ///
    /// The level table is also rebuilt here — the thresholds and caps it used to carry were our own
    /// invention, and a capture of the live hotel contradicts them. See the header of
    /// <c>pet_levels.sql</c>.
    ///
    /// "Shipped empty" was true of a fresh install and not of the live hotel, which had been seeded
    /// by hand. <c>pet_palettes.sql</c> was a plain INSERT, so the first deployment carrying this
    /// migration died on the unique index and rolled back. Both scripts are re-runnable now — the
    /// levels one clears what it owns, the palettes one ignores what is already there — which is
    /// what a seed migration has to be when it cannot know what the database it meets looks like.
    /// </summary>
    [DbContext(typeof(VortexDbContext))]
    [Migration("20260919120000_SeedPetLevelsAndPalettes")]
    public partial class SeedPetLevelsAndPalettes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SeedScripts.Read("pet_levels.sql"));
            migrationBuilder.Sql(SeedScripts.Read("pet_palettes.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference data only. Pets already levelled against these thresholds would be re-read
            // at whatever the previous rows said, so the rollback leaves them in place.
        }
    }
}
