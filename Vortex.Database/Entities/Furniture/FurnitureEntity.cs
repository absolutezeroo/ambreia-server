using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Database.Entities.Furniture;

[Table("furniture")]
[Index(
    nameof(PlayerEntityId),
    nameof(RoomEntityId),
    nameof(WiredChestEntityId),
    nameof(JukeboxEntityId),
    nameof(DeletedAt)
)]
// The playlist read: every disk in one jukebox, in insertion order.
[Index(nameof(JukeboxEntityId))]
public class FurnitureEntity : VortexEntity
{
    [Column("player_id")]
    public int PlayerEntityId { get; set; }

    [Column("definition_id")]
    public int FurnitureDefinitionEntityId { get; set; }

    [Column("room_id")]
    public int? RoomEntityId { get; set; }

    [Column("x")]
    [DefaultValue(0)]
    public int X { get; set; } = 0;

    [Column("y")]
    [DefaultValue(0)]
    public int Y { get; set; } = 0;

    [Column("z", TypeName = "double(10,3)")]
    [DefaultValue(0.0d)]
    public double Z { get; set; }

    [Column("direction")]
    [DefaultValue(Rotation.North)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Rotation Rotation { get; set; }

    [Column("wall_offset")]
    [DefaultValue(0)]
    public int WallOffset { get; set; } = 0;

    [Column("extra_data")]
    public string? ExtraData { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [Column("rentable_space_furniture_id")]
    public int? RentableSpaceFurnitureEntityId { get; set; }

    [Column("wired_chest_id")]
    public int? WiredChestEntityId { get; set; }

    /// <summary>
    /// The jukebox this song disk is loaded into, by furniture id.
    /// </summary>
    /// <remarks>
    /// The same idea as <see cref="WiredChestEntityId" /> and for the same reason: a disk in a
    /// jukebox is still its owner's, but it is not in their hands, and it is not an object in the
    /// room either. One row, one place — so there is no state in which the disk exists twice, and
    /// loading it into a jukebox is a single conditional update rather than a delete and an insert.
    /// <para>
    /// Keyed by the jukebox furniture rather than by the room, which is what lets a playlist survive
    /// the jukebox being picked up and put down again. Deliberately not a foreign key, matching the
    /// chest: the furniture table already deletes rows out from under this kind of reference, and a
    /// constraint would turn that into a failed delete rather than an orphaned disk that the next
    /// read hands back.
    /// </para>
    /// </remarks>
    [Column("jukebox_id")]
    public int? JukeboxEntityId { get; set; }

    [ForeignKey(nameof(FurnitureDefinitionEntityId))]
    public FurnitureDefinitionEntity? FurnitureDefinitionEntity { get; set; }

    [ForeignKey(nameof(RoomEntityId))]
    public RoomEntity? RoomEntity { get; set; }

    [ForeignKey(nameof(RentableSpaceFurnitureEntityId))]
    public FurnitureEntity? RentableSpaceFurnitureEntity { get; set; }
}
