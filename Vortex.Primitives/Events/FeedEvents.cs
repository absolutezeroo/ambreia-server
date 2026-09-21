// The website's social feed.
//
// The only events in this folder raised FOR the notification system rather than found already
// published. Everything else a player is told about -- a lapsed membership, a block, a sale --
// happens in the hotel and was on the bus before notifications existed; the feed is the website's
// own and nothing was announcing it.
//
// Raised all the same, rather than the feed calling the notification sink directly, so that the
// feed stays ignorant of who is listening -- and so a plugin can hear them, which is the point of
// the bus.
namespace Vortex.Primitives.Events;

/// <summary>
/// Somebody wrote <c>@name</c> in a post, and the name resolved to this player.
/// </summary>
/// <remarks>
/// One event per person mentioned, not one carrying a list: the recipients are notified
/// separately, and a list would make every handler loop over it for nothing.
/// </remarks>
public sealed record FeedMentionedEvent(int ActorPlayerId, int MentionedPlayerId, int PostId)
    : IEvent;

/// <summary>
/// Somebody commented on a post, or replied to a comment.
/// </summary>
/// <param name="AuthorPlayerId">Who wrote the thing being answered — the one to tell.</param>
/// <param name="PostId">What was answered, which is where the notification should lead.</param>
public sealed record FeedCommentedEvent(
    int ActorPlayerId,
    int AuthorPlayerId,
    int PostId,
    int CommentId
) : IEvent;

/// <summary>
/// Somebody reposted a post.
/// </summary>
/// <remarks>
/// Raised only when the reaction is LEFT. Withdrawing one raises nothing: the notification for it
/// has already been read, and "X no longer shares your post" is a line nobody needs.
/// </remarks>
public sealed record FeedRepostedEvent(int ActorPlayerId, int AuthorPlayerId, int PostId) : IEvent;
