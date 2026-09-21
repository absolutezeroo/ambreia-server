using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Feed;

/// <summary>One hashtag, once for the whole hotel.</summary>
/// <remarks>
/// Stored lowercased, because that is the search key. The casing the author typed stays in the
/// post's body, which is what the page renders — so <c>#McBurgerFC</c> reads as they wrote it and
/// still finds <c>#mcburgerfc</c>.
/// </remarks>
[Table("web_feed_tags")]
[Index(nameof(Tag), IsUnique = true)]
public class WebFeedTagEntity : VortexEntity
{
    [Column("tag")]
    [MaxLength(TagLength)]
    public required string Tag { get; set; }

    public const int TagLength = 64;

    public ICollection<WebFeedPostTagEntity> Posts { get; set; } = [];
}
