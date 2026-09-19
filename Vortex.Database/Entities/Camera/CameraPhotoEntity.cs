using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;

namespace Vortex.Database.Entities.Camera;

/// <summary>
/// One photo a player took: the row the files on disk hang off, and the record every camera
/// allowance is counted from.
/// </summary>
/// <remarks>
/// <para>
/// Counting the allowances here rather than in grain state is deliberate. A publish cooldown kept in
/// memory is a cooldown that a silo restart hands back to everybody, and a daily competition limit
/// that resets on deactivation is not a limit. A row per photo makes both of them a query, and
/// leaves the grain holding only what is genuinely session state: which photo the player is looking
/// at right now.
/// </para>
/// <para>
/// <see cref="PhotoKey"/> is the filename and the url, so it is minted by the server and unique. It
/// is the only thing guarding a photo once it has been rendered — the client fetches by url and asks
/// nobody's permission — which is why it is random rather than sequential.
/// </para>
/// </remarks>
[Table("camera_photos")]
[Index(nameof(PhotoKey), IsUnique = true)]
[Index(nameof(PlayerEntityId), nameof(CreatedAt))]
public class CameraPhotoEntity : VortexEntity
{
    [Column("photo_key")]
    [MaxLength(64)]
    public required string PhotoKey { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    /// <summary>Where it was shot. Null once the room is gone — a photo outlives the room in it.</summary>
    [Column("room_id")]
    public int? RoomEntityId { get; set; }

    /// <summary>Size of the compressed scene as it arrived, for the operator wondering where the
    /// disk went.</summary>
    [Column("scene_bytes")]
    public required int SceneBytes { get; set; }

    /// <summary>How many sprites were dropped for naming a url the hotel does not allow. Zero on an
    /// honest client; a player whose photos are consistently non-zero is worth a look.</summary>
    [Column("rejected_external_sprites")]
    public required int RejectedExternalSprites { get; set; }

    [Column("purchased_at")]
    public DateTime? PurchasedAt { get; set; }

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("competition_submitted_at")]
    public DateTime? CompetitionSubmittedAt { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [ForeignKey(nameof(RoomEntityId))]
    public RoomEntity? RoomEntity { get; set; }
}
