using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Quests.Snapshots;

namespace Vortex.Primitives.Quests.Grains;

/// <summary>
/// Per-player grain owning that player's quest progress. Reads resolve each quest definition against
/// the player's stored progress into wire-ready snapshots.
/// </summary>
public interface IPlayerQuestGrain : IGrainWithIntegerKey
{
    /// <summary>All non-seasonal quests with the player's progress. <paramref name="openWindow"/> tells
    /// the client whether to pop the quest window open.</summary>
    public Task<QuestListSnapshot> GetQuestsAsync(bool openWindow, CancellationToken ct);

    /// <summary>
    /// Opens the quest window on a named campaign: pushes the full list (window open) and then the
    /// campaign's current quest, so the client's "show details for the next quest" has something to
    /// show. Does not accept anything — that stays the player's own click.
    /// </summary>
    public Task StartCampaignAsync(string campaignCode, CancellationToken ct);

    /// <summary>Seasonal quests only.</summary>
    public Task<QuestListSnapshot> GetSeasonalQuestsAsync(CancellationToken ct);

    /// <summary>The daily quest (if any) plus the easy/hard daily counts.</summary>
    public Task<DailyQuestSnapshot> GetDailyQuestAsync(CancellationToken ct);

    /// <summary>The quest the tracker should show — the player's accepted quest, or null.</summary>
    public Task<QuestSnapshot?> GetTrackedQuestAsync(CancellationToken ct);

    /// <summary>Activates a quest (makes it the single active/tracked quest) and pushes it.</summary>
    public Task ActivateAsync(int questId, CancellationToken ct);

    /// <summary>Accepts a quest — same effect as activating it in this build.</summary>
    public Task AcceptAsync(int questId, CancellationToken ct);

    /// <summary>Cancels the player's currently active quest (the client sends no id).</summary>
    public Task CancelAsync(CancellationToken ct);

    /// <summary>Rejects a specific quest (un-accepts it).</summary>
    public Task RejectAsync(int questId, CancellationToken ct);

    /// <summary>
    /// Advances every accepted, not-yet-completed quest whose type is <paramref name="questType"/> by
    /// <paramref name="amount"/> steps, completing and rewarding those that reach their goal. Driven
    /// from domain-event handlers (room entry, friend added, ...), so <c>[OneWay]</c> for the same
    /// reentrancy reason as achievement progression.
    /// </summary>
    [OneWay]
    public Task ProgressAsync(string questType, int amount, CancellationToken ct);

    /// <summary>
    /// Advances quests of <paramref name="questType"/>, matching the parameterised target: a quest
    /// with no target advances on any occurrence; a quest with a target only advances when
    /// <paramref name="targetType"/>/<paramref name="targetValue"/> match it (e.g. "buy offer 12").
    /// </summary>
    public Task ProgressAsync(
        string questType,
        int amount,
        string? targetType,
        string? targetValue,
        CancellationToken ct
    );

    /// <summary>
    /// Like <see cref="ProgressAsync(string,int,CancellationToken)"/> but advances a matching quest at
    /// most once per calendar day (e.g. a daily-login objective, so 20 reconnections still count once).
    /// </summary>
    public Task ProgressDailyAsync(string questType, int amount, CancellationToken ct);

    /// <summary>
    /// Advances "RoomEntry" quests for a room visit, counting only distinct <em>other</em> players'
    /// rooms — the player's own room is ignored and re-entering a room already visited does not count
    /// again ("visit 10 different rooms").
    /// </summary>
    public Task ProgressRoomVisitAsync(int roomId, DateTime enteredAtUtc, CancellationToken ct);
}
