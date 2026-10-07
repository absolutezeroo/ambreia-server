using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Database.Entities.Loot;

/// <summary>
/// One weighted entry of a <see cref="LootTableEntity"/>. <see cref="Weight"/> is the relative draw
/// chance, so tuning the odds is a database edit rather than a redeploy.
/// </summary>
[Table("loot_table_entries")]
[Index(nameof(LootTableEntityId), nameof(Enabled))]
public class LootTableEntryEntity : VortexEntity
{
    [Column("pool_id")]
    public required int LootTableEntityId { get; set; }

    /// <summary>Variant this entry is restricted to; empty means it can drop from any variant of the
    /// pool. Box colour for the mystery box pool, and whatever the pool declares elsewhere.</summary>
    [Column("variant")]
    [MaxLength(32)]
    [DefaultValue("")]
    public string Variant { get; set; } = string.Empty;

    /// <summary>What is granted. Only <see cref="ProductType.Floor"/>, <see cref="ProductType.Wall"/>,
    /// <see cref="ProductType.Effect"/> and <see cref="ProductType.Premium"/> can be drawn by the
    /// reward window; other types are ignored by the client and rejected at load time.</summary>
    [Column("product_type")]
    [DefaultValue(ProductType.Floor)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ProductType ProductType { get; set; }

    /// <summary>Furniture definition granted for floor/wall prizes (0 for the other types).</summary>
    [Column("furniture_definition_id")]
    [DefaultValue(0)]
    public int FurnitureDefinitionEntityId { get; set; }

    /// <summary>Effect prizes: <c>effectId[:durationSeconds[:subType]]</c>. Membership prizes: number of
    /// months. Unused for floor/wall prizes.</summary>
    [Column("extra_param")]
    [MaxLength(128)]
    [DefaultValue("")]
    public string ExtraParam { get; set; } = string.Empty;

    [Column("weight")]
    [DefaultValue(1)]
    public int Weight { get; set; } = 1;

    [Column("enabled")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [ForeignKey(nameof(LootTableEntityId))]
    public LootTableEntity? LootTableEntity { get; set; }
}
