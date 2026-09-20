using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Events;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.Signals;
using Vortex.Signals.Translators;
using Xunit;

namespace Vortex.Signals.Tests;

/// <summary>
/// The explicit half: one case per branch, for the translators a reflected fixture cannot reach.
/// </summary>
/// <remarks>
/// A generic fixture knows how to make a non-empty string; it will never guess <c>"dance"</c>, nor
/// that a whisper must be false, nor that a rotation needs its own flag. Claiming one fixture proves
/// every shape would be a green test proving nothing — which is the defect this whole subsystem
/// exists to prevent, one level up.
/// </remarks>
public sealed class TranslatorCaseTests
{
    [Fact]
    public void Game_points_name_their_game_and_a_counted_act_rides_alongside()
    {
        GamePointsTranslator translator = new();

        // The points, as an amount: "gaining %limit% participation points" is a sum. Plus the act,
        // as one, because "freezing %limit% Habbos" is a tally — two achievements off one score.
        ImmutableArray<ProgressSignal> frozen = translator.Translate(
            new GamePointsScoredEvent(4312, 7, "freeze", "freeze.player_frozen", 5)
        );

        frozen
            .Should()
            .SatisfyRespectively(
                points =>
                {
                    points.Action.Should().Be(SignalActions.FreezePoints);
                    points.Amount.Should().Be(5);
                },
                act =>
                {
                    act.Action.Should().Be(SignalActions.FreezePlayerFrozen);
                    act.Amount.Should().Be(1);
                }
            );

        // Banzai has no counted act of its own: the points alone.
        translator
            .Translate(new GamePointsScoredEvent(4312, 7, "banzai", "banzai.tile_locked", 8))
            .Should()
            .ContainSingle()
            .Which.Action.Should()
            .Be(SignalActions.BanzaiPoints);

        // Football is the other half alone: a counted act and no points action, because the
        // hotel measures goals and never what a goal was worth.
        ImmutableArray<ProgressSignal> goal = translator.Translate(
            new GamePointsScoredEvent(4312, 7, "football", "football.goal", 3)
        );

        goal.Should().ContainSingle().Which.Action.Should().Be(SignalActions.FootballGoalScored);
        goal[0].Amount.Should().Be(1);

        // A game with no achievements raises nothing rather than an action nothing reads.
        translator
            .Translate(new GamePointsScoredEvent(4312, 7, "football", "football.own_goal", 1))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void A_normal_chat_line_counts_and_a_whisper_does_not()
    {
        ChatTranslator translator = new();

        // The line itself rides along, which is what "say something with the word X in it" reads.
        translator
            .Translate(new PlayerChattedEvent(4312, 7, Whisper: false, "hello casino"))
            .Should()
            .ContainSingle()
            .Which.Facts.Should()
            .ContainSingle(f => f.Key == Facts.Room.Key && f.Value == "7")
            .And.ContainSingle(f => f.Key == Facts.ChatMessage.Key && f.Value == "hello casino");

        // A whisper to yourself would otherwise farm a "chat with users" task.
        translator.Translate(new PlayerChattedEvent(4312, 7, Whisper: true)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("dance", SignalActions.Dance)]
    [InlineData("wave", SignalActions.Wave)]
    public void A_known_gesture_picks_its_own_action(string gesture, string expected)
    {
        new GestureTranslator()
            .Translate(new PlayerGesturedEvent(4312, 7, gesture))
            .Should()
            .ContainSingle()
            .Which.Action.Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("dance")]
    [InlineData("pogo_mogo")]
    [InlineData("duck_funk")]
    [InlineData("the_rollie")]
    public void A_dance_carries_which_dance_it_was(string style)
    {
        // The room is the only place that knows: once the event is raised, the avatar is simply
        // dancing. Dropped here, "do the Rollie" is not a task anyone can write.
        new GestureTranslator()
            .Translate(new PlayerGesturedEvent(4312, 7, "dance", style))
            .Should()
            .ContainSingle()
            .Which.Facts.Should()
            .ContainSingle(f => f.Key == RewardTrackFacts.Dance && f.Value == style);
    }

    [Fact]
    public void A_wave_carries_no_dance()
    {
        // A wave has one form, so the fact would be a question about nothing. Absent rather than
        // empty: a filter only fails closed when the fact is genuinely not there.
        new GestureTranslator()
            .Translate(new PlayerGesturedEvent(4312, 7, "wave"))
            .Should()
            .ContainSingle()
            .Which.Facts.Should()
            .NotContain(f => f.Key == RewardTrackFacts.Dance);
    }

    [Fact]
    public void An_unknown_gesture_raises_nothing()
    {
        new GestureTranslator()
            .Translate(new PlayerGesturedEvent(4312, 7, "cough"))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void A_rotation_is_also_a_move()
    {
        // Deliberate and older than this design: the client has no rotate message, so every rotation
        // has always raised ItemMovedEvent. Dropping move_item for rotations would make existing
        // tasks count less than the day they were written.
        new ItemMovedTranslator()
            .Translate(new ItemMovedEvent(11, 4312, 7, null, RotatedInPlace: true))
            .Select(s => s.Action)
            .Should()
            .BeEquivalentTo([SignalActions.MoveItem, SignalActions.RotateItem]);

        new ItemMovedTranslator()
            .Translate(new ItemMovedEvent(11, 4312, 7, null, RotatedInPlace: false))
            .Should()
            .ContainSingle()
            .Which.Action.Should()
            .Be(SignalActions.MoveItem);
    }

    [Fact]
    public void A_purchase_that_cost_credits_raises_the_spend_as_well()
    {
        CatalogPurchaseTranslator translator = new();
        string operation = Guid.NewGuid().ToString();

        ImmutableArray<ProgressSignal> signals = translator.Translate(
            new CatalogPurchasedEvent(4312, "furni", 99, Quantity: 2, CreditCost: 50, operation)
        );

        signals
            .Select(s => s.Action)
            .Should()
            .BeEquivalentTo([SignalActions.BuyFromCatalogue, SignalActions.SpendCredits]);
        signals.Single(s => s.Action == SignalActions.BuyFromCatalogue).Amount.Should().Be(2);
        signals.Single(s => s.Action == SignalActions.SpendCredits).Amount.Should().Be(50);

        // The identity the whole batch is guarded by. Without it a redelivered purchase would
        // advance every task a second time.
        translator
            .DeliveryIdOf(new CatalogPurchasedEvent(4312, "furni", 99, 1, 50, operation))
            .Should()
            .Be(operation);
    }

    [Fact]
    public void A_free_offer_is_a_purchase_but_not_a_spend()
    {
        new CatalogPurchaseTranslator()
            .Translate(new CatalogPurchasedEvent(4312, "furni", 99, 1, CreditCost: 0, ""))
            .Should()
            .ContainSingle()
            .Which.Action.Should()
            .Be(SignalActions.BuyFromCatalogue);
    }

    [Fact]
    public void Each_side_of_a_trade_is_told_about_the_other()
    {
        ImmutableArray<ProgressSignal> signals = new TradeTranslator().Translate(
            new TradeCompletedEvent(11, 22, [], [], 7)
        );

        signals.Single(s => s.PlayerId == 11).Facts.Should().ContainSingle(f => f.Value == "22");
        signals.Single(s => s.PlayerId == 22).Facts.Should().ContainSingle(f => f.Value == "11");
    }

    [Fact]
    public void Every_badge_worn_gets_its_own_signal()
    {
        new BadgeTranslator()
            .Translate(new BadgesEquippedEvent(4312, ["ACH_A", "ACH_B", "ACH_C"]))
            .Select(s => s.Target)
            .Should()
            .BeEquivalentTo(["ACH_A", "ACH_B", "ACH_C"]);

        new BadgeTranslator().Translate(new BadgesEquippedEvent(4312, [])).Should().BeEmpty();
    }

    [Fact]
    public void A_habbicon_used_in_a_private_conversation_names_no_room()
    {
        // Emitting room 0 would make "any room but 12" match a conversation that happened in no
        // room at all. A filter only fails closed when the fact is genuinely absent.
        new HabbiconUsedTranslator()
            .Translate(new HabbiconUsedEvent(4312, 5, 1, RoomId: 0, ConversationPlayerId: 99))
            .Should()
            .ContainSingle()
            .Which.Facts.Should()
            .NotContain(f => f.Key == Facts.Room.Key);

        new HabbiconUsedTranslator()
            .Translate(new HabbiconUsedEvent(4312, 5, 1, RoomId: 7, ConversationPlayerId: null))
            .Should()
            .ContainSingle()
            .Which.Facts.Should()
            .Contain(f => f.Key == Facts.Room.Key && f.Value == "7");
    }

    [Fact]
    public void A_pet_level_up_carries_the_level_and_credits_the_owner()
    {
        // The amount is the level, for Highest-mode tasks -- not one. And the credit goes to the
        // owner, who need not be whoever fed it.
        ProgressSignal signal = new PetLevelTranslator()
            .Translate(new PetLeveledUpEvent(11, 55, Type: 3, RoomId: 7, Level: 9))
            .Should()
            .ContainSingle()
            .Subject;

        signal.Amount.Should().Be(9);
        signal.PlayerId.Should().Be(11);
        signal.Target.Should().Be("55");
    }

    [Fact]
    public void A_room_creation_carries_the_form_and_omits_an_absent_category()
    {
        ProgressSignal signal = new RoomCreatedTranslator()
            .Translate(new RoomCreatedEvent(11, 7, "Casino Royale", "come in", "model_a", 0))
            .Should()
            .ContainSingle()
            .Subject;

        signal
            .Facts.Should()
            .Contain(f => f.Key == Facts.RoomName.Key && f.Value == "Casino Royale");
        signal.Facts.Should().NotContain(f => f.Key == Facts.Category.Key);
        signal.Target.Should().Be("7");
    }

    [Fact]
    public void Only_letting_somebody_in_counts_as_answering_the_door()
    {
        // Turning people away is not an act to reward, and a task that advanced either way would be
        // farmed by refusing everyone.
        new RoomDoorbellTranslator()
            .Translate(new RoomDoorbellAnsweredEvent(11, 22, 7, Admitted: true))
            .Should()
            .ContainSingle()
            .Which.Target.Should()
            .Be("22");

        new RoomDoorbellTranslator()
            .Translate(new RoomDoorbellAnsweredEvent(11, 22, 7, Admitted: false))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Clearing_a_favourite_guild_is_not_choosing_one()
    {
        new GroupFavouriteTranslator()
            .Translate(new GroupFavouriteChangedEvent(11, GroupId: 5))
            .Should()
            .ContainSingle();

        new GroupFavouriteTranslator()
            .Translate(new GroupFavouriteChangedEvent(11, GroupId: null))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Taking_off_a_collectible_avatar_is_not_wearing_one()
    {
        new NftAvatarTranslator()
            .Translate(new NftAvatarWornEvent(11, CopyId: 9))
            .Should()
            .ContainSingle();

        new NftAvatarTranslator()
            .Translate(new NftAvatarWornEvent(11, CopyId: null))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// The net that keeps the matrix above honest.
    /// </summary>
    /// <remarks>
    /// A branching translator added without a case would otherwise sit untested behind a green
    /// suite, since the structural tests pass happily on a translator that returns nothing. This
    /// fails on any declared shape that no case in this class ever produces.
    /// </remarks>
    [Fact]
    public void Every_declared_shape_is_reached_by_some_case()
    {
        HashSet<string> produced = new(StringComparer.Ordinal);

        foreach (TranslatorUnderTest translator in TranslatorCatalog.All)
        {
            foreach (IEvent probe in Probes(translator.EventType))
            {
                foreach (ProgressSignal signal in translator.Translate(probe))
                {
                    produced.Add(signal.Action);
                }
            }
        }

        ImmutableArray<string> declared =
        [
            .. TranslatorCatalog.All.SelectMany(t => t.Shapes).Select(s => s.Action).Distinct(),
        ];

        declared
            .Should()
            .OnlyContain(
                action => produced.Contains(action),
                "a declared action no case produces is an untested translation"
            );
    }

    /// <summary>
    /// The assertion that would have caught the nine drifts, in the direction they happened.
    /// </summary>
    /// <remarks>
    /// The structural tests check that nothing undeclared is emitted. This checks the opposite and
    /// harder direction: that everything declared actually comes out. That is exactly what went
    /// wrong before — nine actions advertised facts no handler emitted, the editor offered them, and
    /// a filter written on one could never match. Nothing compared the two lists, because one of
    /// them was a file and the other was twenty-two Orleans handlers.
    /// </remarks>
    [Fact]
    public void Every_declared_fact_is_actually_emitted_by_some_case()
    {
        foreach (TranslatorUnderTest translator in TranslatorCatalog.All)
        {
            HashSet<string> emitted = new(StringComparer.Ordinal);

            foreach (IEvent probe in Probes(translator.EventType))
            {
                foreach (ProgressSignal signal in translator.Translate(probe))
                {
                    foreach (SignalFact fact in signal.Facts)
                    {
                        emitted.Add(fact.Key);
                    }
                }
            }

            foreach (FactKey declared in translator.Shapes.SelectMany(s => s.Facts))
            {
                emitted
                    .Should()
                    .Contain(
                        declared.Key,
                        $"{translator.Name} declares '{declared.Key}', so the editor offers it and "
                            + "the validator accepts filters on it -- but nothing emits it"
                    );
            }
        }
    }

    /// <summary>The generic fixture, plus the inputs the branching translators actually need.</summary>
    private static IEnumerable<IEvent> Probes(Type eventType)
    {
        yield return EventFixture.Build(eventType);

        if (eventType == typeof(PlayerGesturedEvent))
        {
            // With a style, because the dance shape declares which dance it was and the generic
            // fixture leaves it empty -- a declared fact nothing emits is a filter that can be
            // written and never matched.
            yield return new PlayerGesturedEvent(4312, 7, "dance", "the_rollie");
            yield return new PlayerGesturedEvent(4312, 7, "wave");
        }

        if (eventType == typeof(ItemMovedEvent))
        {
            yield return new ItemMovedEvent(11, 4312, 7, null, RotatedInPlace: true);
        }

        if (eventType == typeof(CatalogPurchasedEvent))
        {
            yield return new CatalogPurchasedEvent(4312, "furni", 99, 1, CreditCost: 50, "");
        }

        if (eventType == typeof(RoomDoorbellAnsweredEvent))
        {
            yield return new RoomDoorbellAnsweredEvent(11, 22, 7, Admitted: true);
        }

        // The generic fixture builds a non-empty string, which is not a look: a figure is
        // dot-separated parts and anything else carries no set ids at all. Both of these translate
        // the same string two ways, and only the parsed half needs a real one.
        if (eventType == typeof(PlayerFigureChangedEvent))
        {
            yield return new PlayerFigureChangedEvent(
                4312,
                "hd-180-1.ch-210-66.lg-270-82.sh-290-91"
            );
        }

        if (eventType == typeof(PlayerStoodNearFurniEvent))
        {
            yield return new PlayerStoodNearFurniEvent(
                4312,
                99,
                7,
                11,
                "front",
                "hd-180-1.ch-210-66.lg-270-82.sh-290-91"
            );
        }

        if (eventType == typeof(WardrobeOutfitSavedEvent))
        {
            yield return new WardrobeOutfitSavedEvent(
                4312,
                3,
                "hd-180-1.ch-210-66.lg-270-82.sh-290-91"
            );
        }

        if (eventType == typeof(GamePointsScoredEvent))
        {
            // One per branch: the game decides which points action, the scoring act decides
            // whether a second signal counts the act itself.
            yield return new GamePointsScoredEvent(4312, 7, "freeze", "freeze.player_frozen", 5);
            yield return new GamePointsScoredEvent(
                4312,
                7,
                "freeze",
                "freeze.powerup_collected",
                3
            );
            yield return new GamePointsScoredEvent(4312, 7, "banzai", "banzai.tile_locked", 8);
            yield return new GamePointsScoredEvent(4312, 7, "football", "football.goal", 3);
        }
    }
}
