using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Signals;

namespace Vortex.Progression.Quests.Events;

/// <summary>
/// The whole of the bridge from gameplay to daily tasks: one handler, on one event.
/// </summary>
/// <remarks>
/// <para>
/// It replaces seven handlers. Daily tasks keep the quest objective vocabulary — a task with
/// <c>quest_type_code</c> "RoomEntry" counts room entries — so all that lives here is which action
/// means which <see cref="QuestTypes"/> code.
/// </para>
/// <para>
/// They stay separate from campaign quests rather than sharing a consumer: the two systems have
/// different lifetimes (a task lapses at midnight, a quest does not), different codes for the same
/// action, and their own replay-guard keys. Merging them would make a failure in one silently stop
/// the other.
/// </para>
/// </remarks>
public sealed class DailyTaskSignalConsumer(IGrainFactory grainFactory, ICommerceJournal journal)
    : IEventHandler<ProgressSignalsRaised>
{
    /// <summary>
    /// The replay-guard key.
    /// </summary>
    /// <remarks>
    /// Unchanged from <c>DailyTaskCatalogHandler</c>'s, and it must stay unchanged: receipts under
    /// <c>relay:daily-task</c> already exist, and a new key would replay every past purchase.
    /// </remarks>
    private const string CONSUMER = "daily-task";

    /// <summary>
    /// What each action advances. Every one counts <c>1</c>.
    /// </summary>
    /// <remarks>
    /// Including <c>buy_from_catalogue</c>, whose signal carries the quantity: the handler this
    /// replaces passed <c>1</c>, so buying five of something advanced a daily task once. Campaign
    /// quests deliberately do the opposite (§<see cref="QuestSignalConsumer"/>), which is exactly
    /// why the amount is a decision each table makes rather than a property of the signal.
    /// </remarks>
    internal static readonly FrozenDictionary<string, string> Rules = new Dictionary<string, string>
    {
        [SignalActions.EnterOtherUsersRoom] = QuestTypes.RoomEntry,
        [SignalActions.ChangeFigure] = QuestTypes.AvatarLooks,
        [SignalActions.ChangeMotto] = QuestTypes.MottoChange,
        // One signal per side, so both players arrive in the same batch.
        [SignalActions.AcceptFriend] = QuestTypes.FriendListSize,
        [SignalActions.PlaceItem] = QuestTypes.PlaceItem,
        [SignalActions.BuyFromCatalogue] = QuestTypes.CatalogPurchase,
    }.ToFrozenDictionary();

    public async ValueTask HandleAsync(
        ProgressSignalsRaised e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        ImmutableArray<ProgressSignal> mine = Interesting(e.Signals);

        if (mine.IsEmpty)
        {
            return;
        }

        // Once per source event, not per signal: a purchase raises buy_from_catalogue and
        // spend_credits together, and guarding per signal would have the second rejected by the
        // receipt the first wrote.
        if (
            !await CommerceReplayGuard
                .FirstDeliveryAsync(journal, e.DeliveryId, CONSUMER, ct)
                .ConfigureAwait(false)
        )
        {
            return;
        }

        foreach (ProgressSignal signal in mine)
        {
            await grainFactory
                .GetPlayerDailyTaskGrain(signal.PlayerId)
                .ProgressAsync(Rules[signal.Action], 1, ct)
                .ConfigureAwait(false);
        }
    }

    private static ImmutableArray<ProgressSignal> Interesting(
        ImmutableArray<ProgressSignal> signals
    )
    {
        if (signals.IsDefaultOrEmpty)
        {
            return [];
        }

        ImmutableArray<ProgressSignal>.Builder? builder = null;

        foreach (ProgressSignal signal in signals)
        {
            if (signal.PlayerId <= 0 || !Rules.ContainsKey(signal.Action))
            {
                continue;
            }

            builder ??= ImmutableArray.CreateBuilder<ProgressSignal>(signals.Length);
            builder.Add(signal);
        }

        return builder?.ToImmutable() ?? [];
    }
}

/// <summary>Which actions daily tasks care about — the keys of the table above.</summary>
/// <remarks>
/// Static, from the table, rather than built from the <c>quest_type_code</c> of published tasks. The
/// table is the ceiling either way: a published task naming a code no action maps to could never
/// advance, so narrowing the gate to today's content would only add a cache to invalidate.
/// </remarks>
public sealed class DailyTaskSignalInterest : ISignalInterestSource
{
    public ImmutableHashSet<string> Actions { get; } = [.. DailyTaskSignalConsumer.Rules.Keys];
}
