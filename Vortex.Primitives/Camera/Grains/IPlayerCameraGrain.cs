using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Camera.Grains;

/// <summary>
/// One player's camera: the shot they last took, and every allowance that governs taking another.
/// </summary>
/// <remarks>
/// <para>
/// The three confirmations — purchase, publish, competition — carry <em>no arguments at all</em>.
/// The client sends a bare header and means "the photo you are holding for me", so the server has to
/// be holding one. That is the whole reason this grain exists: without somewhere to remember the
/// last accepted scene per player, those three messages cannot be answered at all.
/// </para>
/// <para>
/// It also owns the rate limits, for the same reason a wallet owns a balance: a quota enforced by
/// whoever happens to call is a quota two concurrent calls can both pass. Orleans' single-threaded
/// activation is what makes "have they rendered thirty this hour" a question with one answer.
/// </para>
/// </remarks>
public interface IPlayerCameraGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Accepts a shot, or says why not.
    /// </summary>
    /// <remarks>
    /// Decoding, the limits and the quota all live behind this call rather than in the handler: the
    /// payload is hostile input and the decision to trust it belongs with the state that rate-limits
    /// it, not with the packet that delivered it.
    /// </remarks>
    /// <param name="roomId">Where the session says the player is. A scene naming anywhere else is
    /// refused, whatever the payload claims.</param>
    Task<CameraRenderOutcome> RenderAsync(
        int roomId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct
    );

    /// <summary>
    /// Accepts a shot as the room's thumbnail, or says why not.
    /// </summary>
    /// <remarks>
    /// The same intake as <see cref="RenderAsync"/> and the same hourly allowance — it is the same
    /// drawing, and a player who could spend it on thumbnails instead would simply have two quotas.
    /// What it does not do is keep anything: a thumbnail is not the player's photo, so it is neither
    /// held for the three confirmations nor written to <c>camera_photos</c>. It overwrites whatever
    /// picture the room had, and whether the sender may do that is decided before this is called.
    /// </remarks>
    /// <returns><see cref="CameraRenderRefusal.None"/> when the room's picture was replaced.</returns>
    Task<CameraRenderRefusal> RenderThumbnailAsync(
        int roomId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct
    );

    /// <summary>
    /// Buys the held photo: debits the configured price and puts the item in their inventory.
    /// </summary>
    /// <returns>
    /// False when there is nothing held, it was already bought, or they cannot afford it. The client
    /// has no failure reply for any of those — <c>CameraPurchaseOK</c> has no body and no error twin
    /// — so a false simply means nothing is sent and the widget stays where it was.
    /// </returns>
    Task<bool> PurchaseHeldPhotoAsync(CancellationToken ct);

    /// <summary>Publishes the held photo, subject to the cooldown.</summary>
    Task<CameraPublishOutcome> PublishHeldPhotoAsync(CancellationToken ct);

    /// <summary>Enters the held photo into the running competition, subject to the daily allowance.</summary>
    Task<CameraCompetitionOutcome> SubmitHeldPhotoToCompetitionAsync(CancellationToken ct);
}
