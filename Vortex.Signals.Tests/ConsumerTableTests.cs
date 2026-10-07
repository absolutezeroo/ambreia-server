using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Signals;
using Vortex.Progression.Achievements;
using Vortex.Progression.Achievements.Events;
using Vortex.Progression.Quests.Events;
using Xunit;

namespace Vortex.Signals.Tests;

/// <summary>
/// The three consumer tables: what each signal action means to achievements, daily tasks and
/// campaign quests.
/// </summary>
/// <remarks>
/// <para>
/// These tables replaced twenty-seven event handlers. A handler that quietly stops advancing
/// something is invisible — the bar simply never moves, and nobody reports a bar that was never
/// seen moving — so the mappings are frozen here, by hand, exactly as the fact vocabulary is
/// (<see cref="VocabularyGovernanceTests"/>). Removing an entry has to be typed out.
/// </para>
/// <para>
/// The two tests that matter are not the freezes, though: an action a table names but no translator
/// emits is a dead entry, and a consumer subscribed to an action produced by its own events is a
/// loop. Neither shows up in a build.
/// </para>
/// </remarks>
public sealed class ConsumerTableTests
{
    /// <summary>Every action any consumer maps, with what it does.</summary>
    private static ImmutableArray<string> AllMapped =>
        [
            .. AchievementSignalConsumer
                .Rules.Keys.Concat(DailyTaskSignalConsumer.Rules.Keys)
                .Concat(QuestSignalConsumer.Rules.Keys)
                .Distinct(),
        ];

    [Fact]
    public void Achievements_map_every_action_that_advances_one()
    {
        // The first eight are the inventory of AchievementProgressEventHandlers.cs, which this
        // table replaced; the rest are achievements the catalogue already held and that an already
        // emitted signal means exactly. Anything not here is an achievement whose bar never moves.
        Dictionary<string, (string Name, bool Daily)> expected = new()
        {
            [SignalActions.Login] = (AchievementNames.Login, true),
            [SignalActions.EnterOtherUsersRoom] = (AchievementNames.RoomEntry, false),
            [SignalActions.ChangeMotto] = (AchievementNames.Motto, false),
            [SignalActions.ChangeFigure] = (AchievementNames.AvatarLooks, false),
            [SignalActions.AcceptFriend] = (AchievementNames.FriendListSize, false),
            [SignalActions.PlaceItem] = (AchievementNames.RoomDecoFurniCount, false),
            [SignalActions.ChangeName] = (AchievementNames.Name, false),
            [SignalActions.BuyGift] = (AchievementNames.GiftGiver, false),
            [SignalActions.PetLevel] = (AchievementNames.PetLevelUp, false),
            [SignalActions.AdoptPet] = (AchievementNames.PetLover, false),
            [SignalActions.ReceiveGift] = (AchievementNames.GiftReceiver, false),
        };

        AchievementSignalConsumer
            .Rules.ToDictionary(r => r.Key, r => (r.Value.Name, r.Value.Daily))
            .Should()
            .BeEquivalentTo(expected);
    }

    [Fact]
    public void Daily_tasks_map_the_six_actions_their_handlers_did()
    {
        Dictionary<string, string> expected = new()
        {
            [SignalActions.EnterOtherUsersRoom] = QuestTypes.RoomEntry,
            [SignalActions.ChangeFigure] = QuestTypes.AvatarLooks,
            [SignalActions.ChangeMotto] = QuestTypes.MottoChange,
            [SignalActions.AcceptFriend] = QuestTypes.FriendListSize,
            [SignalActions.PlaceItem] = QuestTypes.PlaceItem,
            [SignalActions.BuyFromCatalogue] = QuestTypes.CatalogPurchase,
        };

        DailyTaskSignalConsumer
            .Rules.ToDictionary(r => r.Key, r => r.Value)
            .Should()
            .BeEquivalentTo(expected);
    }

    [Fact]
    public void Quests_map_the_ten_actions_their_handlers_did()
    {
        // QuestRoomVisitHandler is not a mapping and stays a handler. The two respect actions left
        // with respects (M0 bis).
        QuestSignalConsumer
            .Rules.Keys.Should()
            .BeEquivalentTo([
                SignalActions.AcceptFriend,
                SignalActions.ChangeFigure,
                SignalActions.ChangeMotto,
                SignalActions.CompleteTrade,
                SignalActions.CreateGroup,
                SignalActions.JoinGroup,
                SignalActions.BuyClub,
                SignalActions.Login,
                SignalActions.BuyFromCatalogue,
                SignalActions.PlaceItem,
            ]);

        QuestSignalConsumer.Rules[SignalActions.Login].Daily.Should().BeTrue();
        QuestSignalConsumer
            .Rules[SignalActions.BuyFromCatalogue]
            .Should()
            .BeEquivalentTo(
                new QuestRule(
                    QuestTypes.CatalogPurchase,
                    CountsAmount: true,
                    TargetKey: QuestTypes.TargetOfferId
                )
            );
        QuestSignalConsumer
            .Rules[SignalActions.PlaceItem]
            .Should()
            .BeEquivalentTo(
                new QuestRule(QuestTypes.PlaceItem, TargetKey: QuestTypes.TargetBaseItemId)
            );
    }

    [Fact]
    public void The_same_purchase_counts_once_for_a_daily_task_and_by_quantity_for_a_quest()
    {
        // Not an oversight: DailyTaskCatalogHandler passed 1 and QuestCatalogPurchaseHandler passed
        // the quantity. Buying five of something completes one daily task and advances a quest five
        // times. Pinned because the two tables sit next to each other and look like a typo.
        DailyTaskSignalConsumer.Rules.Should().ContainKey(SignalActions.BuyFromCatalogue);
        QuestSignalConsumer
            .Rules[SignalActions.BuyFromCatalogue]
            .CountsAmount.Should()
            .BeTrue("a quest counts the items bought, a daily task counts the purchase");
    }

    [Fact]
    public void Every_action_a_consumer_maps_is_emitted_by_a_translator()
    {
        // A table entry naming an action nothing raises is a bar that can never move, and the only
        // symptom is silence. This is the same rule as the fact vocabulary's, one level up.
        ImmutableArray<string> emitted =
        [
            .. TranslatorCatalog.All.SelectMany(t => t.Shapes).Select(s => s.Action).Distinct(),
        ];

        AllMapped
            .Should()
            .BeSubsetOf(emitted, "a consumer cannot wait for an action nobody raises");
    }

    [Fact]
    public void A_consumer_never_subscribes_to_an_action_its_own_events_produce()
    {
        // Reward tracks translate QuestCompletedEvent and AchievementLevelUpEvent, which these two
        // consumers produce. If either then subscribed to the action those become, finishing a quest
        // would advance a quest. The loop would be quiet and endless.
        string[] produced = [SignalActions.CompleteQuest, SignalActions.AchievementLevel];

        QuestSignalConsumer
            .Rules.Keys.Should()
            .NotIntersectWith(produced, "a quest finishing must not advance a quest");
        AchievementSignalConsumer
            .Rules.Keys.Should()
            .NotIntersectWith(produced, "an achievement levelling must not advance an achievement");
        DailyTaskSignalConsumer.Rules.Keys.Should().NotIntersectWith(produced);
    }

    [Fact]
    public void The_interest_gate_is_exactly_what_each_consumer_handles()
    {
        // The gate and the mapping are one list read twice. If they could drift, an action would be
        // let through and dropped, or filtered out and never applied — the second silently.
        new AchievementSignalInterest()
            .Actions.Should()
            .BeEquivalentTo(AchievementSignalConsumer.Rules.Keys);
        new DailyTaskSignalInterest()
            .Actions.Should()
            .BeEquivalentTo(DailyTaskSignalConsumer.Rules.Keys);
        new QuestSignalInterest().Actions.Should().BeEquivalentTo(QuestSignalConsumer.Rules.Keys);
    }
}
