using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Context;
using Vortex.Database.Seeds;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Re-applies <c>pet_commands.sql</c>, whose levels, energy costs and experience rewards were
    /// rebuilt against a capture of the live hotel. The script clears the rows it owns first, so a
    /// hotel that already ran the previous seed gets the new numbers rather than a merge of both.
    /// </summary>
    [DbContext(typeof(VortexDbContext))]
    [Migration("20260919170000_RebuildPetCommandsFromCapture")]
    public partial class RebuildPetCommandsFromCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SeedScripts.Read("pet_commands.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference data. Restoring the previous numbers would put a level 2 pet back in reach
            // of commands the hotel does not unlock until past level 8.
        }
    }
}
