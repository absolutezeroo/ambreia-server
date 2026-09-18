using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Lets a quick link start its navigator block collapsed.
    /// <para>
    /// The block already carries a <c>forceClosed</c> flag on the wire and the client obeys it, but
    /// the server had it hardcoded <c>false</c>, so a tab opened every one of its blocks at once.
    /// The official hotel closes five of the seven blocks of <c>official_view</c>; without this
    /// column an operator has no way to say which.
    /// </para>
    /// <para>Defaults to <c>false</c>, so every existing row keeps opening as it does today.</para>
    /// </summary>
    public partial class AddNavigatorQuickLinkForceClosed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "force_closed",
                table: "navigator_quick_links",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "force_closed", table: "navigator_quick_links");
        }
    }
}
