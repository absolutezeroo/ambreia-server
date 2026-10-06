using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Database.Entities.Navigator;

[Table("navigator_top_level_contexts")]
[Index(nameof(SearchCode), IsUnique = true)]
public class NavigatorTopLevelContextEntity : VortexEntity
{
    [Column("search_code")]
    public required string SearchCode { get; set; }

    [Column("visible")]
    [DefaultValue(true)]
    public required bool Visible { get; set; }

    [Column("query_type")]
    [DefaultValue(NavigatorQueryType.None)]
    public NavigatorQueryType QueryType { get; set; } = NavigatorQueryType.None;

    [Column("order_num")]
    [DefaultValue(0)]
    public required int OrderNum { get; set; }

    [InverseProperty("TopLevelContextEntity")]
    public List<NavigatorQuickLinkEntity>? QuickLinks { get; set; }
}
