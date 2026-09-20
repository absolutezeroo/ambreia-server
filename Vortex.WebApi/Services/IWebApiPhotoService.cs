using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

/// <summary>
/// One published photo, as the website's gallery and a profile page draw it.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="Key"/> is <c>CameraPhotoEntity.PhotoKey</c> — the filename, the url and the
/// identity, all three. The site builds its <c>&lt;img&gt;</c> from it against the route
/// <c>WebApiEndpointsCamera</c> owns, so the address of a photo is decided in exactly one place.
/// </para>
/// <para>
/// There is no title and no caption because the hotel's camera records neither: a photo is a room, a
/// player and a moment. habbo.com's own card shows a title for a USER_CREATION and nothing for a
/// PHOTO, which is the same answer arrived at from the other side.
/// </para>
/// <para>
/// <paramref name="Likes"/> is the count and <paramref name="LikedByMe"/> is whether the CALLER is
/// one of them — the two halves the like control renders, and the reason likes are rows rather than
/// a counter. `LikedByMe` is false for a signed-out visitor, which is also what the control shows.
/// </para>
/// <para>
/// <paramref name="Likers"/> is the handful of names the control lists when the thumb is hovered,
/// most recent first. It is CAPPED and the count is not: habbo.com's own popover shows five rows and
/// then "…and N more", so a photo with four hundred likes is still one small answer. The two are
/// allowed to disagree for a second reason as well — a liker who has hidden their profile is counted
/// and not named, which is the same rule this service applies to a photo's owner.
/// </para>
/// </remarks>
public sealed record PhotoSummary(
    string Key,
    string OwnerUniqueId,
    string OwnerName,
    string OwnerFigureString,
    int? RoomId,
    string? RoomName,
    DateTime TakenAt,
    int Likes,
    bool LikedByMe,
    IReadOnlyList<string> Likers
);

/// <summary>One page of the photo gallery.</summary>
public sealed record PhotoPage(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<PhotoSummary> Items
);

/// <summary>
/// The photo reads the website makes.
/// </summary>
/// <remarks>
/// <para>
/// PUBLISHED photos only, everywhere here, and that is the whole access model. A photo the player
/// took and did not publish is private — it costs credits to publish precisely because publishing is
/// the act that makes it public — so an unpublished row must never leave this service. The blob
/// route is a different question and is deliberately open: once a photo IS public, its key is what
/// guards it, which is why the key is thirty-two random characters and not a number.
/// </para>
/// <para>
/// Anonymous, like the profile and appart reads beside it: a gallery is a page a signed-out visitor
/// opens, and a profile links to one from every card.
/// </para>
/// </remarks>
public interface IWebApiPhotoService
{
    /// <summary>
    /// The hotel's gallery: every published photo, newest first. <paramref name="viewerId"/> is the
    /// signed-in player, or <c>null</c>, and decides <see cref="PhotoSummary.LikedByMe"/> only.
    /// </summary>
    Task<PhotoPage> GetPhotosAsync(int page, int pageSize, int? viewerId, CancellationToken ct);

    /// <summary>One published photo by its key, or <c>null</c>.</summary>
    Task<PhotoSummary?> GetPhotoAsync(string key, int? viewerId, CancellationToken ct);

    /// <summary>
    /// Adds or removes the caller's like on a published photo, and answers the photo as it now
    /// stands. <c>null</c> when there is no such published photo.
    /// </summary>
    /// <remarks>
    /// Idempotent in both directions: liking twice is one row and unliking twice is none, which is
    /// what the unique index on (photo, player) buys. A double tap is therefore not a bug to guard
    /// against in a handler.
    /// </remarks>
    Task<PhotoSummary?> SetLikeAsync(string key, int playerId, bool liked, CancellationToken ct);

    /// <summary>
    /// One player's published photos, newest first. An empty list when the player has none, when
    /// they do not exist, or when their profile is hidden — a photo section is not a way to find out
    /// which of the three it was.
    /// </summary>
    Task<IReadOnlyList<PhotoSummary>> GetPlayerPhotosAsync(
        int playerId,
        int limit,
        int? viewerId,
        CancellationToken ct
    );
}
