using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Database.Entities.Furniture;

[Table("furniture")]
[Index(nameof(PlayerEntityId), nameof(RoomEntityId), nameof(DeletedAt))]
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

    [ForeignKey(nameof(FurnitureDefinitionEntityId))]
    public FurnitureDefinitionEntity? FurnitureDefinitionEntity { get; set; }

    [ForeignKey(nameof(RoomEntityId))]
    public RoomEntity? RoomEntity { get; set; }

    [ForeignKey(nameof(RentableSpaceFurnitureEntityId))]
    public FurnitureEntity? RentableSpaceFurnitureEntity { get; set; }
}
