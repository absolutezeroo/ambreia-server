using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Database.Entities.Room;

[Table("rooms")]
public class RoomEntity : VortexEntity
{
    [Column("name")]
    public required string Name { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("door_mode")]
    [DefaultValue(RoomDoorModeType.Open)] // DoorModeType.Open
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required RoomDoorModeType DoorMode { get; set; }

    [Column("password")]
    public string? Password { get; set; }

    [Column("model_id")]
    public required int RoomModelEntityId { get; set; }

    [Column("players_max")]
    [DefaultValue(25)]
    public required int PlayersMax { get; set; }

    // Decoration ids are opaque strings on the wire, not numbers. Landscapes in particular are
    // dotted pairs -- "4.1" and "4.10" are two different backdrops that a double collapses onto the
    // same value, and a float round-trip would also drop the trailing zero the client expects back.
    [Column("paint_wall")]
    [MaxLength(512)]
    public string? PaintWall { get; set; }

    [Column("paint_floor")]
    [MaxLength(512)]
    public string? PaintFloor { get; set; }

    [Column("paint_landscape")]
    [MaxLength(512)]
    public string? PaintLandscape { get; set; }

    [Column("wall_height")]
    [DefaultValue(-1)]
    public required int WallHeight { get; set; }

    [Column("hide_walls")]
    [DefaultValue(false)]
    public required bool HideWalls { get; set; }

    [Column("thickness_wall")]
    [DefaultValue(RoomThicknessType.Normal)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required RoomThicknessType ThicknessWall { get; set; }

    [Column("thickness_floor")]
    [DefaultValue(RoomThicknessType.Normal)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required RoomThicknessType ThicknessFloor { get; set; }

    [Column("allow_blocking")]
    [DefaultValue(false)]
    public required bool AllowBlocking { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public required PlayerEntity PlayerEntity { get; set; }

    [ForeignKey(nameof(RoomModelEntityId))]
    public required RoomModelEntity RoomModelEntity { get; set; }

    [InverseProperty("RoomEntity")]
    public List<RoomRightEntity>? RoomRights { get; set; }
}
