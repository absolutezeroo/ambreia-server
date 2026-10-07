using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Signals;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// The whole of the bridge from gameplay to achievements: one handler, on one event.
/// </summary>
/// <remarks>
/// <para>
/// It replaces eight handlers that each translated one domain event into one achievement name. The
/// translation moved to <c>Vortex.Signals</c>; what is left is the part that is genuinely about
/// achievements — which action means which name, and whether it counts once a day.
/// </para>
/// <para>
/// <see cref="AchievementSignalInterest"/> publishes this table's keys as the interest set, so the
/// gate and the mapping cannot drift: there is one list, and it is this one. That is the point of
/// the whole subsystem, and the reason <c>enter_other_users_room</c> no longer activates a grain on
/// every room entry in a hotel whose content never mentions it.
/// </para>
/// </remarks>
public sealed class AchievementSignalConsumer(IGrainFactory grainFactory, ICommerceJournal journal)
    : IEventHandler<ProgressSignalsRaised>
{
    /// <summary>The replay-guard key. Per consumer, and it must stay that way.</summary>
    private const string CONSUMER = "achievement";

    /// <summary>
    /// What each action advances.
    /// </summary>
    /// <remarks>
    /// These count <c>1</c> unless the rule says <c>CountsAmount</c>: that is what the eight
    /// handlers did, and an achievement here usually measures how often something happened, not how
    /// much of it. The exceptions are the ones the hotel words as a sum — participation points —
    /// and they have to say so, deliberately, exactly as the quest table's <c>CountsAmount</c> does
    /// next door.
    /// </remarks>
    internal static readonly FrozenDictionary<string, AchievementRule> Rules = new Dictionary<
        string,
        AchievementRule
    >
    {
        // Once per calendar day: reconnecting all evening is one login.
        [SignalActions.Login] = new(AchievementNames.Login, Daily: true),
        [SignalActions.EnterOtherUsersRoom] = new(AchievementNames.RoomEntry),
        [SignalActions.ChangeMotto] = new(AchievementNames.Motto),
        [SignalActions.ChangeFigure] = new(AchievementNames.AvatarLooks),
        // The translator emits one signal per side, so both players arrive in the same batch and
        // the loop below credits each. The old handler's Task.WhenAll is gone with them.
        [SignalActions.AcceptFriend] = new(AchievementNames.FriendListSize),
        [SignalActions.PlaceItem] = new(AchievementNames.RoomDecoFurniCount),
        [SignalActions.ChangeName] = new(AchievementNames.Name),
        // "For giving %limit% gifts to other Habbos": the signal is credited to the buyer, which is
        // the player this measures. Its GiftReceiver counterpart is NOT here — open_present credits
        // whoever unwraps a present, including one they bought themselves, and the receiver of a
        // gift is only known as a fact on this same signal. Crediting a signal's target rather than
        // its player is a capability this table does not have.
        [SignalActions.BuyGift] = new(AchievementNames.GiftGiver),
        // One per level gained, which is what "training a pet up %limit% levels" counts. The
        // signal's Amount is the level reached, and is deliberately ignored like every other.
        [SignalActions.PetLevel] = new(AchievementNames.PetLevelUp),
        // Cumulative adoptions against an achievement worded "pets owned". An achievement only ever
        // moves forward (AchievementProgressCalculator), so a count of what is currently held could
        // not be expressed here anyway; releasing a pet does not take the badge back in the hotel
        // either.
        [SignalActions.AdoptPet] = new(AchievementNames.PetLover),
        [SignalActions.ReceiveGift] = new(AchievementNames.GiftReceiver),
    }.ToFrozenDictionary();

    public async ValueTask HandleAsync(
        ProgressSignalsRaised e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        // Filtered first, and materialised, for the two reasons the reward-track consumer documents:
        // a receipt must not be spent on a batch this consumer ignores, and the enumeration is read
        // again after the guard's await.
        ImmutableArray<ProgressSignal> mine = Interesting(e.Signals);

        if (mine.IsEmpty)
        {
            return;
        }

        // No action in the table carries a commerce delivery id today, so this returns true without
        // touching the journal. It is here because adding one later must not silently double-count.
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
            AchievementRule rule = Rules[signal.Action];
            IPlayerAchievementGrain grain = grainFactory.GetPlayerAchievementGrain(signal.PlayerId);

            int amount = rule.CountsAmount ? signal.Amount : 1;

            await (
                rule.Daily
                    ? grain.ProgressDailyAsync(rule.Name, amount, ct)
                    : grain.ProgressAsync(rule.Name, amount, ct)
            ).ConfigureAwait(false);
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

/// <summary>One action's meaning to achievements.</summary>
/// <param name="Name">One of <see cref="AchievementNames"/>.</param>
/// <param name="Daily">Call <c>ProgressDailyAsync</c>: at most one advance per calendar day.</param>
/// <param name="CountsAmount">
/// Credit the signal's amount rather than one. Off by default and deliberately so: an achievement
/// here almost always measures how often something happened, not how much of it, and a signal whose
/// amount is a level reached rather than a quantity earned would multiply a bar by the level.
/// Turned on for the few worded as a sum — "gaining %limit% participation points".
/// </param>
internal readonly record struct AchievementRule(
    string Name,
    bool Daily = false,
    bool CountsAmount = false
);

/// <summary>
/// Which actions achievements care about — the keys of the table above, and nothing else.
/// </summary>
/// <remarks>
/// A dedicated singleton rather than the consumer itself, because handlers are not services: the
/// feature processor registers an activator and builds an instance per invocation, so there would be
/// nothing for the gate to hold. The set is static, unlike reward tracks': achievements are a fixed
/// table, not operator content.
/// </remarks>
public sealed class AchievementSignalInterest : ISignalInterestSource
{
    public ImmutableHashSet<string> Actions { get; } = [.. AchievementSignalConsumer.Rules.Keys];
}
