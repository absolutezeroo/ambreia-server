using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Players.Snapshots;

namespace Vortex.Primitives.Players.Grains;

/// <summary>
/// Per-player grain owning that player's achievement progress. Reads resolve each achievement
/// against the cached definitions into wire-ready snapshots.
/// </summary>
public interface IPlayerAchievementGrain : IGrainWithIntegerKey
{
    public Task<AchievementListSnapshot> GetAchievementsAsync(CancellationToken ct);

    /// <summary>
    /// Adds <paramref name="amount"/> progress to the achievement identified by
    /// <paramref name="achievementName"/> (case-insensitive). Handles level-ups end to end: grants
    /// badges and rewards, updates the achievement score, and pushes the resulting composers. No-ops
    /// for an unknown achievement or a non-positive amount.
    ///
    /// [OneWay]: achievement progression is a fire-and-forget side effect, always driven from a domain
    /// event handler (login, room entry, motto, ...). Its work fans out into grain calls that re-enter
    /// the caller's own grain (e.g. it sends composers / grants badges back through the player-presence
    /// grain that published PlayerEnteredRoomEvent). If the caller awaited this, that non-reentrant
    /// grain would deadlock behind its own event. OneWay dispatches it and returns immediately, so the
    /// originating action completes and frees the grain before the progression's callbacks arrive.
    /// </summary>
    [OneWay]
    public Task ProgressAsync(string achievementName, int amount, CancellationToken ct);

    /// <summary>
    /// Like <see cref="ProgressAsync"/>, but advances the achievement at most once per calendar day —
    /// for daily achievements such as Login, so reconnecting the same day does not keep counting.
    /// Also <c>[OneWay]</c> for the same reentrancy reason.
    /// </summary>
    [OneWay]
    public Task ProgressDailyAsync(string achievementName, int amount, CancellationToken ct);

    /// <summary>
    /// Recounts the distinct colours of one baby pet type this player owns, and raises that
    /// breeding achievement to the count.
    /// </summary>
    /// <remarks>
    /// The only shape here that is not a counter. "Owning %limit% different coloured baby bears" is
    /// a property of the pets table, not a tally of acts: breeding a sixth pet in a colour already
    /// owned advances nothing, and counting adoptions would. So this reads the truth and raises the
    /// bar to it rather than adding to it — and raises only, because an achievement never goes
    /// backwards and releasing a pet does not take a badge away in the hotel either.
    /// <para>
    /// A pet type no breeding achievement covers is a no-op; see <c>BabyPetAchievements</c>.
    /// </para>
    /// </remarks>
    [OneWay]
    public Task ProgressBabyPetColoursAsync(int petType, CancellationToken ct);
}
