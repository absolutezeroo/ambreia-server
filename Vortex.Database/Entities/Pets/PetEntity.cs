using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Database.Entities.Pets;

[Table("pets")]
[Index(nameof(OwnerPlayerEntityId))]
[Index(nameof(RoomEntityId))]
public class PetEntity : VortexEntity
{
    [Column("player_id")]
    public required int OwnerPlayerEntityId { get; set; }

    [Column("room_id")]
    public int? RoomEntityId { get; set; }

    [Column("name")]
    [MaxLength(40)]
    public required string Name { get; set; }

    [Column("type")]
    public required int Type { get; set; }

    [Column("race")]
    public required int Race { get; set; }

    [Column("color")]
    [MaxLength(12)]
    public required string Color { get; set; }

    [Column("gender")]
    [DefaultValue(AvatarGenderType.Male)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required AvatarGenderType Gender { get; set; }

    [Column("level")]
    [DefaultValue(1)]
    public required int Level { get; set; } = 1;

    [Column("experience")]
    [DefaultValue(0)]
    public required int Experience { get; set; }

    /// <summary>A saddle has been fitted, so the pet can be ridden at all.</summary>
    [Column("has_saddle")]
    [DefaultValue(false)]
    public bool HasSaddle { get; set; }

    /// <summary>
    /// The furniture definition of the fitted saddle. Fitting spends the furniture, so this is what
    /// hands one back to the owner when the saddle comes off.
    /// </summary>
    [Column("saddle_definition_id")]
    public int? SaddleDefinitionId { get; set; }

    /// <summary>The owner lets other players ride it. Riding is always allowed to the owner.</summary>
    [Column("riding_permission")]
    [DefaultValue(false)]
    public bool RidingPermission { get; set; }

    [Column("x")]
    public required int X { get; set; }

    [Column("y")]
    public required int Y { get; set; }

    [Column("z", TypeName = "double(10,3)")]
    public required double Z { get; set; }

    [Column("direction")]
    public required int Direction { get; set; }

    [ForeignKey(nameof(OwnerPlayerEntityId))]
    public required PlayerEntity OwnerPlayerEntity { get; set; }

    [ForeignKey(nameof(RoomEntityId))]
    public RoomEntity? RoomEntity { get; set; }
}
