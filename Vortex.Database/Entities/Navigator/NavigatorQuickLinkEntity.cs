using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Database.Entities.Navigator;

[Table("navigator_quick_links")]
public class NavigatorQuickLinkEntity : VortexEntity
{
    [Column("top_level_context_id")]
    public required int TopLevelContextEntityId { get; set; }

    [Column("search_code")]
    public required string SearchCode { get; set; }

    [Column("filter")]
    public string Filter { get; set; } = string.Empty;

    [Column("localization")]
    public string Localization { get; set; } = string.Empty;

    [Column("query_type")]
    [DefaultValue(NavigatorQueryType.None)]
    public NavigatorQueryType QueryType { get; set; } = NavigatorQueryType.None;

    /// <summary>
    /// Whether the block starts collapsed. The official hotel closes the long tail of a tab this
    /// way -- five of the seven blocks of <c>official_view</c> arrive closed -- so a tab does not
    /// dump several hundred rooms at once.
    /// </summary>
    [Column("force_closed")]
    [DefaultValue(false)]
    public bool ForceClosed { get; set; }

    [Column("order_num")]
    [DefaultValue(0)]
    public int OrderNum { get; set; } = 0;

    [ForeignKey(nameof(TopLevelContextEntityId))]
    public NavigatorTopLevelContextEntity? TopLevelContextEntity { get; set; }
}
