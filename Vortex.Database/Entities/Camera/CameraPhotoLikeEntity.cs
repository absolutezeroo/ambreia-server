using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Camera;

/// <summary>
/// One player saying they like one photo.
/// </summary>
/// <remarks>
/// <para>
/// A row per like rather than a counter on the photo, for the reason every counter eventually
/// teaches: a number cannot be un-liked correctly. Two taps from the same player, a retry after a
/// timeout, a double-click — all of them increment a counter and none of them can be taken back,
/// because the counter does not remember who. The unique index on (photo, player) makes liking
/// idempotent at the database rather than in whichever handler happens to run.
/// </para>
/// <para>
/// It also answers the question the card actually asks, which is not "how many" but "how many, and
/// did I". The website's own like control renders its two states off that second half.
/// </para>
/// <para>
/// Unliking DELETES the row. A soft delete would keep the pair and make the unique index refuse the
/// next like — the one case where <see cref="VortexEntity.DeletedAt"/> is the wrong tool.
/// </para>
/// </remarks>
[Table("camera_photo_likes")]
[Index(nameof(CameraPhotoEntityId), nameof(PlayerEntityId), IsUnique = true)]
public class CameraPhotoLikeEntity : VortexEntity
{
    [Column("photo_id")]
    public required int CameraPhotoEntityId { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [ForeignKey(nameof(CameraPhotoEntityId))]
    public CameraPhotoEntity? CameraPhotoEntity { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
