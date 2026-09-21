using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Feed;

/// <summary>A post carrying a tag.</summary>
[Table("web_feed_post_tags")]
[Index(nameof(WebFeedPostEntityId), nameof(WebFeedTagEntityId), IsUnique = true)]
[Index(nameof(WebFeedTagEntityId))]
public class WebFeedPostTagEntity : VortexEntity
{
    [Column("post_id")]
    public required int WebFeedPostEntityId { get; set; }

    [Column("tag_id")]
    public required int WebFeedTagEntityId { get; set; }

    [ForeignKey(nameof(WebFeedPostEntityId))]
    public WebFeedPostEntity? WebFeedPostEntity { get; set; }

    [ForeignKey(nameof(WebFeedTagEntityId))]
    public WebFeedTagEntity? WebFeedTagEntity { get; set; }
}
