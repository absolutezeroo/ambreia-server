using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Feed;

/// <summary>One player's answer to one poll.</summary>
/// <remarks>
/// The post is carried alongside the choice, and it is redundant only in the sense that it could be
/// reached through the choice. Carrying it is what makes the unique index expressible at all:
/// "one vote per player per poll" is an index over (post, player), and without the post column it
/// would be a subquery in a constraint, which is to say a rule enforced by whichever handler
/// happens to run rather than by the database.
/// </remarks>
[Table("web_feed_poll_votes")]
[Index(nameof(WebFeedPostEntityId), nameof(PlayerEntityId), IsUnique = true)]
public class WebFeedPollVoteEntity : VortexEntity
{
    [Column("post_id")]
    public required int WebFeedPostEntityId { get; set; }

    [Column("choice_id")]
    public required int WebFeedPollChoiceEntityId { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [ForeignKey(nameof(WebFeedPostEntityId))]
    public WebFeedPostEntity? WebFeedPostEntity { get; set; }

    [ForeignKey(nameof(WebFeedPollChoiceEntityId))]
    public WebFeedPollChoiceEntity? WebFeedPollChoiceEntity { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
