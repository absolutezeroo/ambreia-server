using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities.Navigator;

/// <summary>
/// One promoted-rooms group in the official rooms view — the horizontal strip the client titles
/// "Play Freeze" or "Party hard" and expands into a top ten.
/// </summary>
/// <remarks>
/// It carries no room list and no figure on purpose. Which rooms a group holds is decided by
/// <see cref="SearchCode"/>, resolved through the same vocabulary a quick link uses, so a group
/// keeps itself current instead of going stale the day a picked room is deleted; and the avatar the
/// client draws beside the heading is the top room's owner, which is derived, never typed.
/// </remarks>
[Table("navigator_promoted_categories")]
public class NavigatorPromotedCategoryEntity : VortexEntity
{
    /// <summary>The client's own group code. It localizes
    /// <c>promotedroomcategory.&lt;code&gt;</c> with no fallback, so anything outside
    /// <c>NavigatorPromotedCategoryCodes.Known</c> renders as the raw key.</summary>
    [Column("code")]
    [MaxLength(64)]
    public required string Code { get; set; }

    /// <summary>Which rooms fill the group, as a navigator search code (<c>staffpicks</c>,
    /// <c>category__7</c>, <c>popular</c>, …).</summary>
    [Column("search_code")]
    [MaxLength(64)]
    public required string SearchCode { get; set; }

    /// <summary>Optional filter handed to the search, for the codes that take one.</summary>
    [Column("filter")]
    [MaxLength(128)]
    public string Filter { get; set; } = string.Empty;

    [Column("order_num")]
    [DefaultValue(0)]
    public int OrderNum { get; set; }

    [Column("visible")]
    [DefaultValue(true)]
    public bool Visible { get; set; } = true;
}
