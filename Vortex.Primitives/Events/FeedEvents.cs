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

/// <summary>
/// Somebody liked a post.
/// </summary>
/// <remarks>
/// The commonest interaction on the feed by a wide margin, and the last one to get an event —
/// the first three were written to a list that did not mention it. Same rule as the repost:
/// only a NEW like is announced, never the withdrawal of one.
/// </remarks>
public sealed record FeedFavouritedEvent(int ActorPlayerId, int AuthorPlayerId, int PostId)
    : IEvent;

/// <summary>
/// A post changed in a way every reader should see: it was published, answered, reacted to, voted
/// in, or deleted.
/// </summary>
/// <param name="RootPostId">
/// The ROOT of the thread, never the comment inside it. A reader's feed is a list of roots, so
/// the root is the card that has to be refreshed — and refreshing it brings the comment with it.
/// </param>
/// <remarks>
/// Public where the three above are personal: this says "the feed moved", not "you were named".
/// Nothing about who it concerns, because it concerns everybody reading.
/// </remarks>
public sealed record FeedChangedEvent(int RootPostId) : IEvent;
