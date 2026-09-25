using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Context;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Gives the three room decorations their furni category: wallpaper 2, floor 3, landscape 4.
    /// </summary>
    /// <remarks>
    /// The Arcturus import gave them interaction_type <c>roomeffect</c>, which the converter's
    /// category test did not recognise, so all three landed as Default (1). The client decides from
    /// that category alone whether an inventory item is placed as furni or applied to the room
    /// (<c>FurniModel.as</c>: category 2/3/4 sends RequestRoomPropertySet), so a bought wallpaper
    /// was dragged onto the wall as an ordinary item. Wall-type rows only (<c>type = 1</c>): the
    /// names are generic enough that a floor item could share one.
    /// </remarks>
    [DbContext(typeof(VortexDbContext))]
    [Migration("20260925090000_CategorizeRoomDecorations")]
    public partial class CategorizeRoomDecorations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                UPDATE furniture_definitions SET category = 2 WHERE name = 'wallpaper' AND type = 1;
                UPDATE furniture_definitions SET category = 3 WHERE name = 'floor' AND type = 1;
                UPDATE furniture_definitions SET category = 4 WHERE name = 'landscape' AND type = 1;
                """
            );

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                UPDATE furniture_definitions SET category = 1
                WHERE name IN ('wallpaper', 'floor', 'landscape') AND type = 1;
                """
            );
    }
}
