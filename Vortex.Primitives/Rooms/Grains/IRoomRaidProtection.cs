using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.RaidProtection;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>
/// A room's defence against being flooded by a coordinated crowd: the owner's settings, and the
/// live judgement the entry path asks for on every arrival.
/// </summary>
/// <remarks>
/// Only the settings half of this exists in any Habbo client. What makes a room decide it is being
/// raided is server-side and unpublished, so the detector behind
/// <see cref="EvaluateEntryAsync" /> is this emulator's design. See <see cref="RaidDetectionSensitivity" />.
/// </remarks>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomRaidProtection")]
public interface IRoomRaidProtection : IGrainWithIntegerKey
{
    /// <summary>
    /// This room's settings for a viewer who is allowed to see them, or <c>null</c> when they are
    /// not. Null is the whole answer: the official client silently drops a settings packet from a
    /// room it has no management rights in, so there is nothing useful to send a refused viewer.
    /// </summary>
    [ReadOnly]
    public Task<RoomRaidProtectionSnapshot?> GetRaidProtectionAsync(PlayerId viewerId);

    /// <summary>
    /// Applies a draft from the settings panel.
    /// </summary>
    /// <param name="confirmed">
    /// Whether the player confirmed the client's "a raid is happening right now" prompt. The client
    /// only sets it on an off-to-on transition, so this cannot be required — it is an audit fact,
    /// and a gate on exactly one case (see <see cref="RaidProtectionSaveResult.ConfirmationRequired" />).
    /// </param>
    public Task<RaidProtectionSaveOutcome> SaveRaidProtectionAsync(
        ActionContext actorCtx,
        RoomRaidProtectionSnapshot draft,
        bool confirmed,
        CancellationToken ct
    );

    /// <summary>
    /// Judges one arrival: let them in, turn them away, or turn them away and ban them — and, in the
    /// same answer, whether they get the protection button once inside.
    /// </summary>
    /// <remarks>
    /// Called on the entry path before the player is committed to the room, and it counts. Calling
    /// it twice for one arrival would count that arrival twice and bring the threshold down on
    /// honest traffic.
    /// </remarks>
    public Task<RaidEntryDecision> EvaluateEntryAsync(PlayerId playerId, CancellationToken ct);
}
