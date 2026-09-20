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
/// </remarks>
public sealed record PhotoSummary(
    string Key,
    string OwnerUniqueId,
    string OwnerName,
    string OwnerFigureString,
    int? RoomId,
    string? RoomName,
    DateTime TakenAt
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
    /// <summary>The hotel's gallery: every published photo, newest first.</summary>
    Task<PhotoPage> GetPhotosAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// One player's published photos, newest first. An empty list when the player has none, when
    /// they do not exist, or when their profile is hidden — a photo section is not a way to find out
    /// which of the three it was.
    /// </summary>
    Task<IReadOnlyList<PhotoSummary>> GetPlayerPhotosAsync(
        int playerId,
        int limit,
        CancellationToken ct
    );
}
