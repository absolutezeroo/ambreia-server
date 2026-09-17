using System.Threading;
using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Quests.Grains;

/// <summary>
/// Cluster-wide singleton owning the active community goal: its ladder, the hotel total, and every
/// player's contribution. Single-threaded by design — contributions arrive from every quest
/// completion in the hotel, and a shared running total is exactly the state Orleans grains exist to
/// serialise without locks.
/// </summary>
public interface ICommunityGoalGrain : IGrainWithStringKey
{
    /// <summary>Sends the goal widget's state to one player. No-op when no goal is active.</summary>
    public Task SendProgressAsync(int playerId, CancellationToken ct);

    /// <summary>
    /// Sends the leaderboard to one player. <paramref name="limit"/> comes from the caller so the
    /// cap is configuration, not a magic number baked into the grain.
    /// </summary>
    public Task SendHallOfFameAsync(int playerId, int limit, CancellationToken ct);

    /// <summary>
    /// Adds to a player's contribution and to the hotel total. Ignored when no goal is active or the
    /// active one has expired.
    /// </summary>
    /// <param name="completions">
    /// How many quests were finished, NOT how much score to add. The goal owns the conversion — its
    /// <c>score_per_quest</c> — because that number is what an operator turns to make the bar fill
    /// faster or slower without rewriting every threshold on the ladder. Callers count events; the
    /// goal decides what an event is worth.
    /// </param>
    public Task ContributeAsync(
        int playerId,
        string campaignCode,
        int completions,
        CancellationToken ct
    );

    /// <summary>Re-reads goals, levels and totals from the database.</summary>
    public Task ReloadAsync(CancellationToken ct);
}
