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
using Vortex.Primitives.Quests.Grains;
using Vortex.Primitives.Signals;

namespace Vortex.Progression.Quests.Events;

/// <summary>
/// The bridge from gameplay to campaign quests: one handler, on one event.
/// </summary>
/// <remarks>
/// <para>
/// It replaces twelve of the thirteen quest handlers. The thirteenth,
/// <c>QuestRoomEntryHandler</c>, stays: it does not map an action to a code but calls
/// <c>ProgressRoomVisitAsync</c>, which needs the entry's own timestamp to deduplicate against the
/// room-entry log. A timestamp is not a fact anybody filters on, so putting it on the signal to
/// serve one call site would pollute the vocabulary the whole subsystem exists to keep honest.
/// </para>
/// <para>
/// Two other paths remain outside this one, deliberately:
/// <c>IPlayerQuestGrain.ProgressAsync</c> is also called straight from four packet handlers, which
/// is a breach of "packet handlers orchestrate only" that predates this rework and is a subject of
/// its own.
/// </para>
/// </remarks>
public sealed class QuestSignalConsumer(IGrainFactory grainFactory, ICommerceJournal journal)
    : IEventHandler<ProgressSignalsRaised>
{
    /// <summary>
    /// The replay-guard key.
    /// </summary>
    /// <remarks>
    /// Unchanged from <c>QuestCatalogPurchaseHandler</c>'s. Receipts under <c>relay:quest</c>
    /// already exist; a new key would replay every past purchase.
    /// </remarks>
    private const string CONSUMER = "quest";

    /// <summary>What each action advances.</summary>
    internal static readonly FrozenDictionary<string, QuestRule> Rules = new Dictionary<
        string,
        QuestRule
    >
    {
        // One signal per side, both in the same batch.
        [SignalActions.AcceptFriend] = new(QuestTypes.FriendListSize),
        [SignalActions.ChangeFigure] = new(QuestTypes.AvatarLooks),
        [SignalActions.ChangeMotto] = new(QuestTypes.MottoChange),
        // Both players, two signals.
        [SignalActions.CompleteTrade] = new(QuestTypes.TradeCompleted),
        [SignalActions.CreateGroup] = new(QuestTypes.CreateGroup),
        [SignalActions.JoinGroup] = new(QuestTypes.JoinGroup),
        [SignalActions.BuyMembership] = new(QuestTypes.BuyMembership),
        // Once per calendar day.
        [SignalActions.Login] = new(QuestTypes.Login, Daily: true),
        // The only two that carry a target, and in both the value is the signal's own target: the
        // offer bought, the furniture type placed. So a quest can require a specific one.
        [SignalActions.BuyFromCatalogue] = new(
            QuestTypes.CatalogPurchase,
            CountsAmount: true,
            TargetKey: QuestTypes.TargetOfferId
        ),
        [SignalActions.PlaceItem] = new(
            QuestTypes.PlaceItem,
            TargetKey: QuestTypes.TargetBaseItemId
        ),
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
            QuestRule rule = Rules[signal.Action];
            IPlayerQuestGrain grain = grainFactory.GetPlayerQuestGrain(signal.PlayerId);

            if (rule.Daily)
            {
                await grain.ProgressDailyAsync(rule.Code, 1, ct).ConfigureAwait(false);

                continue;
            }

            await grain
                .ProgressAsync(
                    rule.Code,
                    rule.CountsAmount ? signal.Amount : 1,
                    rule.TargetKey,
                    rule.TargetKey is null ? null : signal.Target,
                    ct
                )
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

/// <summary>One action's meaning to campaign quests.</summary>
/// <param name="Code">One of <see cref="QuestTypes"/>.</param>
/// <param name="Daily">Call <c>ProgressDailyAsync</c>: at most one advance per calendar day.</param>
/// <param name="CountsAmount">
/// Advance by the signal's amount rather than by one. True only for catalogue purchases, where the
/// handler this replaces passed the quantity — buying five advances a quest five times. Daily tasks
/// deliberately count the same purchase once.
/// </param>
/// <param name="TargetKey">
/// The key the signal's target is passed under, or null when the action has no target a quest can
/// require.
/// </param>
internal readonly record struct QuestRule(
    string Code,
    bool Daily = false,
    bool CountsAmount = false,
    string? TargetKey = null
);

/// <summary>Which actions campaign quests care about — the keys of the table above.</summary>
public sealed class QuestSignalInterest : ISignalInterestSource
{
    public ImmutableHashSet<string> Actions { get; } = [.. QuestSignalConsumer.Rules.Keys];
}
