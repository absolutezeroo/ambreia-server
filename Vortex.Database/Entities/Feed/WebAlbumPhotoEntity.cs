using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Camera;

namespace Vortex.Database.Entities.Feed;

/// <summary>
/// One photo in an album, in the order it was added.
/// </summary>
/// <remarks>
/// The photo row, not its key, for the reason <see cref="WebFeedPostPhotoEntity"/> gives: every read
/// re-applies "published and not deleted", so a photo its owner takes back leaves the album instead
/// of staying in it as a broken image. The pair is unique — a photo is in an album once.
/// </remarks>
[Table("web_album_photos")]
[Index(nameof(WebAlbumEntityId), nameof(CameraPhotoEntityId), IsUnique = true)]
[Index(nameof(WebAlbumEntityId), nameof(Ordinal))]
public class WebAlbumPhotoEntity : VortexEntity
{
    [Column("album_id")]
    public required int WebAlbumEntityId { get; set; }

    [Column("photo_id")]
    public required int CameraPhotoEntityId { get; set; }

    [Column("ordinal")]
    public required int Ordinal { get; set; }

    [ForeignKey(nameof(WebAlbumEntityId))]
    public WebAlbumEntity? WebAlbumEntity { get; set; }

    [ForeignKey(nameof(CameraPhotoEntityId))]
    public CameraPhotoEntity? CameraPhotoEntity { get; set; }
}
