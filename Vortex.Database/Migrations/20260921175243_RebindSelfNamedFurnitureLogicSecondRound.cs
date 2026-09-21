using Microsoft.EntityFrameworkCore.Migrations;
using Vortex.Database.Seeds;

#nullable disable

namespace Vortex.Database.Migrations
{
    /// <summary>
    /// Three more definitions the asset-derived binding pass bound away from their own behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same repair as <see cref="RebindSelfNamedFurnitureLogic"/>, on three rows that round did
    /// not name. It re-applies the same file: the script is guarded by <c>logic &lt;&gt; name</c>, so
    /// the rows already put right are skipped and only the newly listed ones move. A name added to
    /// the seed needs a migration like this one, because a migration that has run does not run
    /// again.
    /// </para>
    /// <para>
    /// What they carried instead: <c>wf_act_rel_mov</c> (relative furni move) had
    /// <c>wf_act_bot_give_handitem_or_effect</c>, and the two <c>wf_act_remove_furni_inventory</c>
    /// rows had <c>wf_act_give_points_type</c> and <c>wf_act_unhide_items</c>. No class registers
    /// any of those three keys, so the furni fell through to the family default and did nothing —
    /// while the box each one is, relative move and remove-furni, was written and had no furni in
    /// the hotel that could reach it.
    /// </para>
    /// <para>
    /// Two near misses were checked and left alone: the furni named <c>wf_cnd_has_altitude</c> and
    /// <c>wf_cnd_has_handitem</c> carry <c>wf_cnd_has_same_height</c> and
    /// <c>wf_cnd_wears_handitem</c>, and those are the *same* classes — both names are already
    /// declared on them, so the rows bind correctly and rebinding would be churn. A name that is a
    /// registered key is not on its own a reason to rebind it.
    /// </para>
    /// </remarks>
    public partial class RebindSelfNamedFurnitureLogicSecondRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SeedScripts.Read("furni_logic_self_named_rebind.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The value being replaced is the one that made the furni do somebody else's job. There
            // is no working state to go back to.
        }
    }
}
