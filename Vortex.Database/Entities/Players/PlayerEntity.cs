using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Room;
using Vortex.Database.Entities.Security;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Database.Entities.Players;

[Table("players")]
[Index(nameof(Name), IsUnique = true)]
public class PlayerEntity : VortexEntity
{
    [Column("account_id")]
    public int? PlayerAccountEntityId { get; set; }

    [ForeignKey(nameof(PlayerAccountEntityId))]
    public PlayerAccountEntity? PlayerAccount { get; set; }

    [Column("name")]
    public required string Name { get; set; }

    [Column("motto")]
    public string? Motto { get; set; }

    [Column("figure")]
    [MaxLength(FigureString.MaxLength)]
    [DefaultValue("hr-115-42.hd-195-19.ch-3030-82.lg-275-1408.fa-1201.ca-1804-64")]
    public required string Figure { get; set; }

    [Column("gender")]
    [DefaultValue(AvatarGenderType.Male)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required AvatarGenderType Gender { get; set; }

    [Column("status")]
    [DefaultValue(PlayerStatusType.Offline)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required PlayerStatusType PlayerStatus { get; set; }

    [Column("perk_flags")]
    [DefaultValue(PlayerPerkFlags.None)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required PlayerPerkFlags PlayerPerks { get; set; }

    /// <summary>The score the client shows on the avatar (sent on every user change). Nothing writes
    /// it since achievements were removed: it is read as stored, 0 for every new player.</summary>
    [Column("achievement_score")]
    [DefaultValue(0)]
    public int AchievementScore { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<FurnitureEntity>? Furniture { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<SecurityTicketEntity>? SecurityTickets { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomEntity>? Rooms { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomRightEntity>? RoomRights { get; set; }
}
