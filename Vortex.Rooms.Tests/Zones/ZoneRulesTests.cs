using FluentAssertions;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones.Compilation;
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Tests.Support;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// What a cell allows (plan D6, D24): a Safe or NoCombat area is safe, a PvP area allows PvP, a
/// Hostile area — or a dungeon — is hostile, and any other area follows the zone; the highest
/// priority area that rules combat decides. NoMount and Restricted hold wherever such an area
/// lies; a switched-off area rules nothing.
/// </summary>
public sealed class ZoneRulesTests
{
    private static ZoneRuntimeState Zone(bool pvp, params ZoneArea[] areas) =>
        Zone(pvp, ZoneType.World, areas);

    private static ZoneRuntimeState Zone(bool pvp, ZoneType type, params ZoneArea[] areas) =>
        new(
            ZoneCompiler.Compile(
                ZoneSamples.Minimal() with
                {
                    Settings = new ZoneSettings
                    {
                        Id = "village",
                        Name = "Village",
                        PvpAllowed = pvp,
                        Type = type,
                        RecommendedLevel = 3,
                    },
                    Zones = [.. areas],
                }
            )
        );

    [Fact]
    public void ASafeArea_IsSafeAndRefusesPvp()
    {
        ZoneRuntimeState zone = Zone(true, Area("inn", ZoneAreaKinds.Safe, "Auberge", 0, 2));

        (ZoneRuleBook.At(zone, 1, 1).Safe, ZoneRuleBook.At(zone, 1, 1).Pvp)
            .Should()
            .Be((true, false));
        (ZoneRuleBook.At(zone, 4, 1).Safe, ZoneRuleBook.At(zone, 4, 1).Pvp)
            .Should()
            .Be((false, true));
    }

    [Fact]
    public void APvpArea_AllowsPvpInAPeacefulZone()
    {
        ZoneRuntimeState zone = Zone(false, Area("arena", ZoneAreaKinds.PvP, "Arène", 0, 2));

        ZoneRuleBook.At(zone, 1, 1).Pvp.Should().BeTrue();
        ZoneRuleBook.At(zone, 4, 1).Pvp.Should().BeFalse();
    }

    [Fact]
    public void AnotherKind_FollowsTheZone()
    {
        ZoneRuntimeState zone = Zone(true, Area("town", ZoneAreaKinds.Town, "Ville", 0, 5));

        (ZoneRuleBook.At(zone, 1, 1).Safe, ZoneRuleBook.At(zone, 1, 1).Pvp)
            .Should()
            .Be((false, true));
    }

    [Fact]
    public void TheHigherPriority_Decides()
    {
        ZoneRuntimeState zone = Zone(
            false,
            Area("arena", ZoneAreaKinds.PvP, "Arène", 0, 5, priority: 1),
            Area("shelter", ZoneAreaKinds.Safe, "Abri", 0, 2, priority: 2)
        );

        ZoneRuleBook.At(zone, 1, 1).Safe.Should().BeTrue();
        ZoneRuleBook.At(zone, 1, 1).Pvp.Should().BeFalse();
        ZoneRuleBook.At(zone, 4, 1).Pvp.Should().BeTrue();
    }

    [Fact]
    public void AHostileArea_IsHostile_AndASafeOneNever()
    {
        ZoneRuntimeState zone = Zone(
            false,
            Area("woods", ZoneAreaKinds.Hostile, "Bois", 0, 5, priority: 1),
            Area("camp", ZoneAreaKinds.Safe, "Camp", 0, 1, priority: 2)
        );

        ZoneRuleBook.At(zone, 4, 1).Hostile.Should().BeTrue();
        ZoneRuleBook.At(zone, 0, 1).Hostile.Should().BeFalse();
    }

    [Fact]
    public void ADungeon_IsHostile_SaveWhereAnAreaSaysOtherwise()
    {
        ZoneRuntimeState zone = Zone(
            false,
            ZoneType.Dungeon,
            Area("rest", ZoneAreaKinds.Safe, "Repos", 0, 1)
        );

        ZoneRuleBook.At(zone, 4, 1).Hostile.Should().BeTrue();
        ZoneRuleBook.At(zone, 0, 1).Hostile.Should().BeFalse();
        ZoneRuleBook.At(zone, 4, 1).Type.Should().Be(ZoneType.Dungeon);
    }

    [Fact]
    public void NoMountAndRestricted_HoldWhereverTheyLie()
    {
        ZoneRuntimeState zone = Zone(
            false,
            Area("halls", ZoneAreaKinds.NoMount, "Halles", 0, 2),
            Area("vault", ZoneAreaKinds.Restricted, "Coffre", 2, 3)
        );

        ZoneCellRules both = ZoneRuleBook.At(zone, 2, 1);

        (both.NoMount, both.Restricted).Should().Be((true, true));
        (ZoneRuleBook.At(zone, 5, 1).NoMount, ZoneRuleBook.At(zone, 5, 1).Restricted)
            .Should()
            .Be((false, false));
    }

    [Fact]
    public void TheAreasLevel_OverridesTheZones()
    {
        ZoneRuntimeState zone = Zone(
            false,
            Area("den", ZoneAreaKinds.Boss, "Antre", 0, 1) with
            {
                RecommendedLevel = 12,
            }
        );

        ZoneRuleBook.At(zone, 0, 1).RecommendedLevel.Should().Be(12);
        ZoneRuleBook.At(zone, 4, 1).RecommendedLevel.Should().Be(3);
    }

    [Fact]
    public void ASwitchedOffArea_RulesNothing()
    {
        ZoneRuntimeState zone = Zone(true, Area("inn", ZoneAreaKinds.Safe, "Auberge", 0, 2));
        zone.SetAreaActive("inn", false);

        ZoneRuleBook.At(zone, 1, 1).Safe.Should().BeFalse();
        ZoneRuleBook.At(zone, 1, 1).Pvp.Should().BeTrue();
    }

    [Fact]
    public void TheCachedRules_FollowAnAreaSwitchedOffAndOnAgain()
    {
        // Worked out once per cell, then read: a switch must not leave the old answer behind.
        ZoneRuntimeState zone = Zone(true, Area("inn", ZoneAreaKinds.Safe, "Auberge", 0, 2));

        zone.RulesAt(1, 1).Safe.Should().BeTrue();

        zone.SetAreaActive("inn", false);

        zone.RulesAt(1, 1).Safe.Should().BeFalse();
        zone.ActiveAreasAt(1, 1).Should().BeEmpty();

        zone.SetAreaActive("inn", true);

        zone.RulesAt(1, 1).Safe.Should().BeTrue();
        zone.ActiveAreasAt(1, 1).Should().ContainSingle();
    }

    [Fact]
    public void TheSameCell_IsWorkedOutOnce()
    {
        ZoneRuntimeState zone = Zone(true, Area("inn", ZoneAreaKinds.Safe, "Auberge", 0, 2));

        zone.RulesAt(1, 1).Should().BeSameAs(zone.RulesAt(1, 1));
    }
}
