using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Players;

/// <summary>
/// One saved wardrobe outfit slot for a player (the avatar-editor wardrobe). The client shows a
/// fixed grid of slots gated by Membership/VIP, but the server just persists whatever figure/gender the
/// client saves per slot and echoes them back on <c>GetWardrobe</c>.
/// </summary>
[Table("player_wardrobe_outfits")]
[Index(nameof(PlayerEntityId), nameof(SlotId), IsUnique = true)]
public class PlayerWardrobeOutfitEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("slot_id")]
    public required int SlotId { get; set; }

    [Column("figure")]
    [MaxLength(255)]
    public required string Figure { get; set; }

    [Column("gender")]
    [MaxLength(8)]
    public required string Gender { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
