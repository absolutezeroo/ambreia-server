using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Camera;

namespace Vortex.Database.Entities.Feed;

/// <summary>
/// One of the author's photos, attached to one of their posts, in the order they chose.
/// </summary>
/// <remarks>
/// A foreign key to <c>camera_photos</c> and not the photo's key as a string. The key would be
/// enough to build the url, and that is exactly the trap: the projection has to re-apply the photo
/// service's own conditions — published, not deleted, owner not hiding — so a photo the owner
/// later unpublishes disappears from the post instead of staying in it as a broken image. That
/// re-check needs the row, so the row is what is stored.
/// </remarks>
[Table("web_feed_post_photos")]
[Index(nameof(WebFeedPostEntityId), nameof(Ordinal), IsUnique = true)]
public class WebFeedPostPhotoEntity : VortexEntity
{
    [Column("post_id")]
    public required int WebFeedPostEntityId { get; set; }

    [Column("photo_id")]
    public required int CameraPhotoEntityId { get; set; }

    [Column("ordinal")]
    public required int Ordinal { get; set; }

    [ForeignKey(nameof(WebFeedPostEntityId))]
    public WebFeedPostEntity? WebFeedPostEntity { get; set; }

    [ForeignKey(nameof(CameraPhotoEntityId))]
    public CameraPhotoEntity? CameraPhotoEntity { get; set; }
}
