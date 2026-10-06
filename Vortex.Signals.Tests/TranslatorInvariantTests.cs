using System;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;
using Xunit;

namespace Vortex.Signals.Tests;

/// <summary>
/// The structural half of the test that was missing: what must hold for every translator, whatever
/// it produces.
/// </summary>
/// <remarks>
/// These four hold even when a translator returns nothing, which is the normal outcome when a
/// generic fixture guesses a branching translator's input wrong. What a generic fixture cannot do —
/// prove that each declared fact is actually emitted — is the explicit matrix in
/// <see cref="TranslatorCaseTests"/>, and the coverage test there is what stops that matrix from
/// quietly falling behind.
/// </remarks>
public sealed class TranslatorInvariantTests
{
    public static TheoryData<TranslatorUnderTest> Translators => TranslatorCatalog.AsTheoryData();

    /// <summary>The events that fed RewardTrackEventHandlers, named rather than counted.</summary>
    /// <remarks>
    /// A count would drift upwards with every new translator and stop meaning anything. These are
    /// the ones the port had to carry over; losing any of them silently stops content that already
    /// depends on it. The two Habbicon events left this list with the feature (RPG fork, M0).
    /// </remarks>
    private static readonly string[] PortedFromHandlers =
    [
        "PlayerEnteredRoomEvent",
        "RoomCreatedEvent",
        "PlayerChattedEvent",
        "PlayerGesturedEvent",
        "ItemPlacedEvent",
        "ItemMovedEvent",
        "ItemPickedUpEvent",
        "PlayerWalkedOnFurniEvent",
        "FriendRequestSentEvent",
        "RespectGivenEvent",
        "MessengerMessageSentEvent",
        "PlayerFigureChangedEvent",
        "PlayerMottoChangedEvent",
        "BadgesEquippedEvent",
        "CatalogPurchasedEvent",
        "TradeCompletedEvent",
        "PetLeveledUpEvent",
        "QuestCompletedEvent",
        "AchievementLevelUpEvent",
    ];

    [Fact]
    public void Every_domain_event_that_fed_a_reward_track_still_has_a_translator()
    {
        TranslatorCatalog
            .All.Select(t => t.EventType.Name)
            .Should()
            .Contain(PortedFromHandlers, "the port must not have dropped one on the way");
    }

    [Theory]
    [MemberData(nameof(Translators))]
    public void A_translator_emits_no_fact_it_did_not_declare(TranslatorUnderTest translator)
    {
        ImmutableHashSet<string> declared = translator
            .Shapes.SelectMany(s => s.Facts.Select(f => f.Key))
            .ToImmutableHashSet(StringComparer.Ordinal);

        foreach (ProgressSignal signal in translator.Translate(Fixture(translator)))
        {
            foreach (SignalFact fact in signal.Facts)
            {
                // The host adds this one after the translator has run, so it is legitimately not in
                // any shape's fact list -- the shape declares its type through TargetKind instead.
                if (string.Equals(fact.Key, Facts.TargetKey, StringComparison.Ordinal))
                {
                    continue;
                }

                declared
                    .Should()
                    .Contain(
                        fact.Key,
                        $"{translator.Name} emitted '{fact.Key}', which the editor will never offer "
                            + "and the content validator will refuse"
                    );
            }
        }
    }

    [Theory]
    [MemberData(nameof(Translators))]
    public void A_translator_emits_no_action_it_did_not_declare(TranslatorUnderTest translator)
    {
        ImmutableHashSet<string> declared = translator
            .Shapes.Select(s => s.Action)
            .ToImmutableHashSet(StringComparer.Ordinal);

        foreach (ProgressSignal signal in translator.Translate(Fixture(translator)))
        {
            declared.Should().Contain(signal.Action);
        }
    }

    [Theory]
    [MemberData(nameof(Translators))]
    public void A_translator_never_emits_an_empty_value(TranslatorUnderTest translator)
    {
        foreach (ProgressSignal signal in translator.Translate(Fixture(translator)))
        {
            // An absent fact must be absent, not present and blank: a filter fails closed only on a
            // genuinely missing fact, and "" would match an empty expected value by accident.
            signal.Facts.Should().OnlyContain(f => !string.IsNullOrEmpty(f.Value));
            signal.Target.Should().NotBe(string.Empty);
        }
    }

    [Theory]
    [MemberData(nameof(Translators))]
    public void A_translator_credits_a_real_player(TranslatorUnderTest translator)
    {
        foreach (ProgressSignal signal in translator.Translate(Fixture(translator)))
        {
            // Zero is the system, and progress credited to the system is progress lost. Every
            // consumer drops these, so emitting one is a translator reading the wrong field.
            signal.PlayerId.Should().BePositive();
        }
    }

    [Theory]
    [MemberData(nameof(Translators))]
    public void Every_declared_action_is_a_known_one(TranslatorUnderTest translator)
    {
        foreach (SignalShape shape in translator.Shapes)
        {
            SignalActions
                .All.Should()
                .Contain(
                    shape.Action,
                    $"{translator.Name} declares an action no content could ever name"
                );
        }
    }

    [Theory]
    [MemberData(nameof(Translators))]
    public void A_closed_fact_declares_what_may_be_chosen(TranslatorUnderTest translator)
    {
        foreach (FactKey fact in translator.Shapes.SelectMany(s => s.Facts))
        {
            if (fact.Kind == FactKind.Enum)
            {
                fact.EnumValues.Should()
                    .NotBeEmpty($"'{fact.Key}' would render as a select with no options");
            }
        }
    }

    private static IEvent Fixture(TranslatorUnderTest translator) =>
        EventFixture.Build(translator.EventType);
}
