using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Navigator.Enums;
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

    [Column("users_now")]
    [DefaultValue(0)]
    public required int UsersNow { get; set; }

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

    [Column("allow_pets")]
    [DefaultValue(false)]
    public required bool AllowPets { get; set; }

    [Column("allow_pets_eat")]
    [DefaultValue(false)]
    public required bool AllowPetsEat { get; set; }

    [Column("leave_on_door_tile")]
    [DefaultValue(false)]
    public bool LeaveOnDoorTile { get; set; }

    [Column("idle_sleep_enabled")]
    [DefaultValue(true)]
    public bool IdleSleepEnabled { get; set; } = true;

    [Column("idle_sleep_timeout_seconds")]
    [DefaultValue(300)]
    public int IdleSleepTimeoutSeconds { get; set; } = 300;

    [Column("idle_autokick_enabled")]
    [DefaultValue(false)]
    public bool IdleAutokickEnabled { get; set; }

    [Column("idle_autokick_timeout_seconds")]
    [DefaultValue(1800)]
    public int IdleAutokickTimeoutSeconds { get; set; } = 1800;

    [Column("mute_all_pets")]
    [DefaultValue(false)]
    public bool MuteAllPets { get; set; }

    [Column("trade_type")]
    [DefaultValue(RoomTradeModeType.Disabled)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required RoomTradeModeType TradeType { get; set; }

    [Column("mute_type")]
    [DefaultValue(ModSettingType.Owner)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ModSettingType MuteType { get; set; }

    [Column("kick_type")]
    [DefaultValue(ModSettingType.Owner)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ModSettingType KickType { get; set; }

    [Column("ban_type")]
    [DefaultValue(ModSettingType.Owner)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ModSettingType BanType { get; set; }

    [Column("chat_mode_type")]
    [DefaultValue(ChatModeType.FreeFlow)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ChatModeType ChatModeType { get; set; }

    [Column("chat_bubble_type")]
    [DefaultValue(ChatBubbleWidthType.Normal)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ChatBubbleWidthType ChatBubbleType { get; set; }

    [Column("chat_speed_type")]
    [DefaultValue(ChatScrollSpeedType.Normal)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ChatScrollSpeedType ChatSpeedType { get; set; }

    [Column("chat_flood_type")]
    [DefaultValue(ChatFloodSensitivityType.Minimal)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ChatFloodSensitivityType ChatFloodType { get; set; }

    [Column("chat_distance")]
    [DefaultValue(50)]
    public required int ChatDistance { get; set; }

    [Column("last_active")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public DateTime LastActive { get; set; }

    [Column("tag_1")]
    [MaxLength(25)]
    public string? Tag1 { get; set; }

    [Column("tag_2")]
    [MaxLength(25)]
    public string? Tag2 { get; set; }

    [Column("score")]
    [DefaultValue(0)]
    public required int Score { get; set; }

    /// <summary>1-based position in the hotel's top-rated-rooms table, 0 when the room is outside
    /// it. The client labels it "Top rated rooms rank:" (<c>navigator.roomranking</c>) and hides the
    /// field entirely when it is not positive, so 0 is the honest "unranked" value. Derived from
    /// <see cref="Score"/> by <c>RoomDirectoryGrain</c>'s flush, never edited by hand.</summary>
    [Column("ranking")]
    [DefaultValue(0)]
    public int Ranking { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public required PlayerEntity PlayerEntity { get; set; }

    [ForeignKey(nameof(RoomModelEntityId))]
    public required RoomModelEntity RoomModelEntity { get; set; }

    [InverseProperty("RoomEntity")]
    public List<RoomRightEntity>? RoomRights { get; set; }
}
