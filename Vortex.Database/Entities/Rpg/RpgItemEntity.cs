using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Rpg.Items.Enums;

namespace Vortex.Database.Entities.Rpg;

/// <summary>
/// One copy of an item a character holds (spec Objets §5): a stack is one row. Written by
/// <c>PlayerCharacterGrain</c> alone; its <see cref="DefinitionId"/> is rewritten by an import's
/// replace rule, never by the grain.
/// </summary>
[Table("rpg_items")]
[Index(nameof(PlayerEntityId))]
[Index(nameof(DefinitionId))]
public class RpgItemEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("definition_id")]
    [MaxLength(ContentIdLength)]
    public required string DefinitionId { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    /// <summary>Null for an item that never wears out.</summary>
    [Column("durability")]
    public int? Durability { get; set; }

    [Column("bound")]
    public bool Bound { get; set; }

    [Column("container")]
    [DefaultValue(ItemContainer.Bag)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ItemContainer Container { get; set; }

    /// <summary>The bag case, the equipment slot, or the parcel's number.</summary>
    [Column("slot")]
    public int Slot { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
