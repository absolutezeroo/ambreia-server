using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.World;

/// <summary>A zone built by the map editor; its documents are in <see cref="ZoneVersionEntity"/>.</summary>
[Table("zones")]
[Index(nameof(Key), IsUnique = true)]
[Index(nameof(RoomEntityId), IsUnique = true)]
public class ZoneEntity : VortexEntity
{
    /// <summary>The document's settings.id (<c>village_ambre</c>).</summary>
    [Column("zone_key")]
    [MaxLength(ContentIdLength)]
    public required string Key { get; set; }

    /// <summary>The room that runs the zone.</summary>
    [Column("room_id")]
    public required int RoomEntityId { get; set; }

    /// <summary>The version players get; null until the first publication.</summary>
    [Column("published_version")]
    public int? PublishedVersion { get; set; }

    [Column("locked_by_account_id")]
    public int? LockedByAccountId { get; set; }

    [Column("locked_at")]
    public DateTime? LockedAt { get; set; }
}
