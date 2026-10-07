namespace Vortex.Primitives.Fishing;

/// <summary>
/// Why a fishing request was refused.
/// </summary>
/// <remarks>
/// <para>
/// This is for a request that should not have been made. A fish that simply escaped is not an error:
/// it is the ordinary outcome of a catch roll and never reaches the client as one.
/// </para>
/// <para>
/// The values are the contract with vortex-modern-client's <c>FishingErrorCode</c> and are
/// <strong>append-only</strong> — a retired code is never reused, or an older client shows the wrong
/// reason for a refusal it cannot name.
/// </para>
/// </remarks>
public enum FishingErrorCode
{
    /// <summary>The furni clicked is not a fishing spot in any known zone.</summary>
    NotASpot = 0,

    /// <summary>The player's fishing level is below the zone's requirement.</summary>
    LevelTooLow = 1,

    /// <summary>The daily currency cap is reached — fishing on is pointless until it resets.</summary>
    DailyCapReached = 2,

    /// <summary>A session is already running, or the spot was only just started on.</summary>
    TooSoon = 3,

    /// <summary>The sighting or record id is unknown, expired, or belongs to somebody else.</summary>
    UnknownSighting = 4,

    // 5 was DerbyClosed, removed with the fishing derby (M0 bis). Never reuse it.

    /// <summary>
    /// The player is not standing next to the spot. Checked server-side because the client sends a
    /// furni id and nothing else: without this, any spot in the room can be fished from anywhere in
    /// it, which is what shipped.
    /// </summary>
    TooFarAway = 6,
}
