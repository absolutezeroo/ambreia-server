using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

/// <summary>
/// An album as a card shows it: the feed's, and the profile's list. <paramref name="Photos"/> holds
/// the first few, <paramref name="PhotoCount"/> all of them.
/// </summary>
public sealed record FeedAlbum(
    int Id,
    string Title,
    string Description,
    int PhotoCount,
    IReadOnlyList<PhotoSummary> Photos
);

/// <summary>One album in full, for its own page.</summary>
/// <remarks>
/// <paramref name="FeedPostId"/> is the album's card: its likes, shares and comments, and the id the
/// page deletes the album through.
/// </remarks>
public sealed record Album(
    int Id,
    int FeedPostId,
    string OwnerUniqueId,
    string OwnerName,
    string OwnerFigureString,
    string Title,
    string Description,
    DateTime CreatedAt,
    bool Mine,
    IReadOnlyList<PhotoSummary> Photos
);

/// <summary>What the composer's Album tab sends.</summary>
public sealed record AlbumCreateRequest(
    string Title,
    string? Description,
    IReadOnlyList<string>? PhotoKeys
);

/// <summary>A new title and description for an existing album.</summary>
public sealed record AlbumEditRequest(string Title, string? Description);

/// <summary>Photos to add to an album, as keys — see <see cref="FeedPublishRequest"/>.</summary>
public sealed record AlbumPhotosRequest(IReadOnlyList<string>? PhotoKeys);

/// <summary>Why an album write was refused, or <see cref="None"/>.</summary>
public enum AlbumRefusal
{
    None = 0,

    /// <summary>No such album, deleted, or not the caller's — one answer, see the endpoints.</summary>
    NotFound,
    TitleInvalid,
    DescriptionTooLong,

    /// <summary>A create with no photos: the card would be empty.</summary>
    Empty,
    PhotoNotYours,
    Full,
    WordFiltered,
}

/// <summary>
/// The website's photo albums: a player's named collections of their own published photos.
/// </summary>
/// <remarks>
/// <para>
/// Public, like the gallery and the feed: every photo in an album is already a published photo, and
/// the album is published the moment it is created, as a card in the feed.
/// </para>
/// <para>
/// There is no delete here. An album lives and dies with its card, so it is deleted by deleting the
/// card (<see cref="IWebApiFeedService.DeleteAsync"/>, behind the second factor) or by a staff
/// takedown of it — one path, not two to keep in step.
/// </para>
/// </remarks>
public interface IWebApiAlbumService
{
    /// <summary>One album and its photos, or null when it is not readable.</summary>
    Task<Album?> GetAsync(int id, int? viewerId, CancellationToken ct);

    /// <summary>A player's albums, newest first.</summary>
    Task<IReadOnlyList<FeedAlbum>> ListAsync(int ownerId, CancellationToken ct);

    /// <summary>Creates an album and posts its card to the feed.</summary>
    Task<(AlbumRefusal Refusal, Album? Album)> CreateAsync(
        int playerId,
        AlbumCreateRequest body,
        CancellationToken ct
    );

    /// <summary>Renames the caller's album. The card follows; it does not come back up.</summary>
    Task<(AlbumRefusal Refusal, Album? Album)> EditAsync(
        int playerId,
        int albumId,
        AlbumEditRequest body,
        CancellationToken ct
    );

    /// <summary>
    /// Adds photos to the caller's album and brings its card back to the top of the feed. A photo
    /// already in it is skipped, so a retried request adds nothing twice.
    /// </summary>
    Task<(AlbumRefusal Refusal, Album? Album)> AddPhotosAsync(
        int playerId,
        int albumId,
        AlbumPhotosRequest body,
        CancellationToken ct
    );

    /// <summary>Takes one photo out of the caller's album. Absent already is not an error.</summary>
    Task<(AlbumRefusal Refusal, Album? Album)> RemovePhotoAsync(
        int playerId,
        int albumId,
        string photoKey,
        CancellationToken ct
    );
}
