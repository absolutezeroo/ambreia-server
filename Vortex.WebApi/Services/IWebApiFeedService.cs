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
    IReadOnlyList<FeedPost> Comments
);

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
}

/// <summary>
/// The website's social feed.
/// </summary>
/// <remarks>
/// <para>
/// Every read here carries the same two conditions and they live in one place on purpose: the post
/// is not deleted, and its author is not hiding their profile. The second is the one worth keeping
/// in mind — a player who has hidden their profile has said they do not want to be browsed, and a
/// feed that still lists their posts under their name is the same disclosure by another door. It is
/// the rule <c>IWebApiPhotoService</c> states for the gallery, for the same reason.
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
}
