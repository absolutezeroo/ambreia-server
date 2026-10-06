using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;

namespace Vortex.Database.Entities.Feed;

/// <summary>One player reposting or favouriting one post.</summary>
/// <remarks>
/// <para>
/// A row per reaction rather than a counter on the post: a number cannot be un-reacted correctly,
/// because it does not remember who. Two
/// taps, a retry after a timeout, a double click — each increments a counter and none of them can
/// be taken back.
/// </para>
/// <para>
/// Withdrawing a reaction DELETES the row. A soft delete would keep the pair and make the unique
/// index refuse the next one — the case where <see cref="VortexEntity.DeletedAt"/> is the wrong
/// tool.
/// </para>
/// </remarks>
[Table("web_feed_reactions")]
[Index(nameof(WebFeedPostEntityId), nameof(PlayerEntityId), nameof(Kind), IsUnique = true)]
public class WebFeedReactionEntity : VortexEntity
{
    [Column("post_id")]
    public required int WebFeedPostEntityId { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("kind")]
    public required FeedReactionKind Kind { get; set; }

    [ForeignKey(nameof(WebFeedPostEntityId))]
    public WebFeedPostEntity? WebFeedPostEntity { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
