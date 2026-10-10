using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Characters.Zones;
using Vortex.Rpg.Zones;
using Vortex.Rpg.Zones.Rules;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec Z1 §4.4: the zone conditions the characters module declares, read from the facts.</summary>
public sealed class CharacterZoneConditionsTests
{
    private static ZoneRuleContext Ctx(CharacterZoneFacts? facts) =>
        new(
            new PlayerId(7),
            facts is null
                ? new Dictionary<Type, ZoneFacts>()
                : new Dictionary<Type, ZoneFacts> { [typeof(CharacterZoneFacts)] = facts },
            DateTimeOffset.Now,
            FakeProxy.Create<IZoneRuntime>(_ => null),
            NoZoneSimulation.Instance
        );

    private static CharacterZoneFacts Facts(int level, int keys = 0) =>
        new()
        {
            Level = level,
            Items =
                keys > 0
                    ? ImmutableDictionary<string, int>.Empty.Add("item.key", keys)
                    : ImmutableDictionary<string, int>.Empty,
        };

    [Theory]
    [InlineData(3, 2, 0, true)]
    [InlineData(3, 2, 3, true)]
    [InlineData(3, 4, 0, false)]
    [InlineData(5, 2, 4, false)]
    public void PlayerLevel_IsBetweenMinAndMax_ZeroMeaningNoCap(
        int level,
        int min,
        int max,
        bool holds
    ) =>
        new PlayerLevelEvaluator()
            .Holds(new PlayerLevelCondition(min, max), Ctx(Facts(level)))
            .Should()
            .Be(holds);

    [Fact]
    public void PlayerLevel_WithoutFacts_IsFalse() =>
        new PlayerLevelEvaluator()
            .Holds(new PlayerLevelCondition(1, 0), Ctx(null))
            .Should()
            .BeFalse();

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    public void HasItem_NeedsTheCount_AtLeastOne(int owned, int count, bool holds) =>
        new HasItemEvaluator()
            .Holds(new HasItemCondition("item.key", count), Ctx(Facts(1, owned)))
            .Should()
            .Be(holds);

    [Fact]
    public void HasItem_WithoutFacts_IsFalse() =>
        new HasItemEvaluator()
            .Holds(new HasItemCondition("item.key", 1), Ctx(null))
            .Should()
            .BeFalse();
}
