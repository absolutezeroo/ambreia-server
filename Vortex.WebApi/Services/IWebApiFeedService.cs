using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Feed;

namespace Vortex.WebApi.Services;

/// <summary>One answer a poll offers, with how many took it.</summary>
public sealed record FeedPollChoice(int Id, string Label, int Votes);

/// <summary>
/// A post's poll. The percentages the page draws are computed there from
/// <paramref name="Votes"/> over <paramref name="TotalVotes"/>; the server sends counts, because a
/// percentage is a rendering decision and a count is a fact.
/// </summary>
public sealed record FeedPoll(
    int TotalVotes,
    int? MyChoiceId,
    IReadOnlyList<FeedPollChoice> Choices
);

/// <summary>
/// One entry in the feed — or one comment, which is the same shape by construction.
/// </summary>
/// <remarks>
/// Flat, the way <see cref="PhotoSummary"/> is flat, rather than nesting an author object: the two
/// travel together on the same page and a reader should not have to hold two conventions.
/// <paramref name="Comments"/> holds the three most recent on a root and is empty on a comment —
/// the thread is one level deep, so a comment has none by definition.
/// </remarks>
public sealed record FeedPost(
    int Id,
    string AuthorUniqueId,
    string AuthorName,
    string AuthorFigureString,
    int? GroupId,
    string? GroupName,
    string? GroupBadgeCode,
    FeedPostKind Kind,
    string Body,
    DateTime CreatedAt,
    IReadOnlyList<PhotoSummary> Photos,
    FeedPoll? Poll,
    IReadOnlyList<string> Tags,
    int CommentCount,
    int RepostCount,
    int FavoriteCount,
    bool RepostedByMe,
    bool FavoritedByMe,
    bool Mine,
    IReadOnlyList<FeedPost> Comments,
    FeedReposter? Reposter = null
);

/// <summary>
/// Who put a post back into the feed, when the entry is a repost rather than the original.
/// </summary>
/// <remarks>
/// <para>
/// Belongs to the feed ENTRY, not to the post, which is why it is null everywhere else: the same
/// post appears once as its author's original and once per player who shared it, and only the
/// sharing entries carry this. A post read by id never has one — it is the original.
/// </para>
/// <para>
/// The name and figure are copied rather than left to the reader to look up, for the same reason
/// the author's are: the card has to draw without a second request.
/// </para>
/// </remarks>
public sealed record FeedReposter(string Name, string FigureString, DateTime At);

/// <summary>One page of the feed.</summary>
public sealed record FeedPage(int Page, int PageSize, int Total, IReadOnlyList<FeedPost> Items);

/// <summary>What the composer sends.</summary>
/// <remarks>
/// Photos arrive as KEYS because that is what the browser knows — the gallery hands it keys and
/// <c>photoUrl</c> builds a url out of one. The service resolves each to a row and stores the row's
/// id; a key that resolves to nothing is a refusal, never a quietly skipped photo.
/// </remarks>
public sealed record FeedPublishRequest(
    FeedPostKind Kind,
    string Body,
    IReadOnlyList<string>? PhotoKeys,
    IReadOnlyList<string>? PollChoices,
    int? ParentId,
    int? GroupId
);

/// <summary>Why a publish was refused, or <see cref="None"/>.</summary>
public enum FeedRefusal
{
    None = 0,
    BodyEmpty,
    BodyTooLong,
    TooManyPhotos,
    PhotoNotYours,
    PollInvalid,
    ParentNotFound,
    ParentNotRoot,
    GroupNotMember,
    KindMismatch,

    /// <summary>The hotel-wide word filter refused the body or a poll choice.</summary>
    WordFiltered,
}

/// <summary>
/// The website's social feed.
/// </summary>
/// <remarks>
/// <para>
/// Every read here carries the same condition and it lives in one place on purpose: the post is
/// not deleted. That is the whole of it.
/// </para>
/// <para>
/// In particular the feed does NOT gate on the author's <c>ProfileVisible</c>, and this paragraph
/// used to claim that it did. The column defaults to <c>false</c> here — the hotel honours
/// habbo.com's "your profile is private by default" — so copying the gallery's rule across left the
/// feed empty for nearly every player, and made <c>PublishAsync</c> answer 200 with a null body,
/// because it reads its own new row back through this same gate. Publishing is the consent:
/// posting to a public feed is itself an act of publication, where <c>ProfileVisible</c> governs
/// whether someone may browse your profile, which is a different question.
/// <see cref="IWebApiPhotoService"/> keeps that rule for the gallery deliberately — browsing
/// someone's photos under their name IS browsing them.
/// </para>
/// <para>
/// Anonymous, like the gallery and the profile beside it. <c>viewerId</c> is the signed-in player
/// or null, and it decides only the "…ByMe" fields and <c>Mine</c>.
/// </para>
/// </remarks>
public interface IWebApiFeedService
{
    /// <summary>Root posts, newest first. <paramref name="tag"/> filters to one hashtag.</summary>
    Task<FeedPage> GetFeedAsync(
        int page,
        int pageSize,
        string? tag,
        int? viewerId,
        CancellationToken ct
    );

    /// <summary>One post with all of its comments, or null when it is not readable.</summary>
    Task<FeedPost?> GetPostAsync(int id, int? viewerId, CancellationToken ct);

    /// <summary>
    /// Publishes a post, or a comment when <see cref="FeedPublishRequest.ParentId"/> is set.
    /// </summary>
    /// <remarks>
    /// Every field is validated here and nowhere else, so the endpoint stays a translation from a
    /// refusal to a status code and the rules cannot be bypassed by a second caller.
    /// </remarks>
    Task<(FeedRefusal Refusal, FeedPost? Post)> PublishAsync(
        int playerId,
        FeedPublishRequest body,
        CancellationToken ct
    );

    /// <summary>
    /// Records or moves one player's vote, and answers the poll as it now stands. Null when the
    /// post is not a readable poll, or when the choice belongs to a different one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vote is MOVED, never added to. The unique index on (post, player) says the same thing at
    /// the database, but EF's in-memory provider does not enforce a unique index, so the rule has
    /// to hold here as well as there — and a second row would not merely double-count, it would
    /// make the index refuse the next honest vote on a real MySQL.
    /// </para>
    /// <para>
    /// The choice is checked to belong to the poll named in the route. Without that, a choice id
    /// is just a number a browser sends, and a vote could be aimed at any choice in the hotel and
    /// would land silently on a poll nobody meant.
    /// </para>
    /// </remarks>
    Task<FeedPost?> VoteAsync(int playerId, int postId, int choiceId, CancellationToken ct);

    /// <summary>
    /// Leaves or withdraws one reaction, and answers the post as it now stands. Null when the post
    /// is not readable.
    /// </summary>
    /// <remarks>
    /// Takes the WANTED state rather than toggling. A toggle sent twice — a flaky connection
    /// retrying, a double tap — lands on the opposite of what the reader pressed, and they have no
    /// way to tell which of the two happened. With the state in the body a retry is a no-op, and
    /// that is why both branches tolerate finding the row already in the state asked for.
    /// </remarks>
    Task<FeedPost?> SetReactionAsync(
        int playerId,
        int postId,
        FeedReactionKind kind,
        bool on,
        CancellationToken ct
    );

    /// <summary>
    /// Soft-deletes the caller's own post, and its comments with it. False when the post is not
    /// theirs or is already gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Soft, so the row survives for the audit trail the rest of this hotel keeps. Because
    /// <c>Readable(db)</c> is the single gate, one <c>DeletedAt</c> is what removes the post from
    /// the feed, from a direct read, from voting and from reacting, all at once.
    /// </para>
    /// <para>
    /// The comments go too, including other people's. A comment is unreadable out of the thread it
    /// answers, and one left live would sit in nobody's feed — the feed lists roots only — and be
    /// reachable by nobody. Their rows survive as well.
    /// </para>
    /// </remarks>
    Task<bool> DeleteAsync(int playerId, int postId, CancellationToken ct);
}
