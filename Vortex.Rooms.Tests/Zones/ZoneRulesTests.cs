using FluentAssertions;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones;
using Vortex.Tests.Support;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Plan D6: a Safe or NoCombat area is safe, a PvP area allows PvP, any other area — and no area —
/// follows the zone's own setting; the highest priority area that rules decides.
/// </summary>
public sealed class ZoneRulesTests
{
    private static CompiledZone Zone(bool pvp, params ZoneArea[] areas) =>
        ZoneCompiler.Compile(
            ZoneSamples.Minimal() with
            {
                Settings = new ZoneSettings
                {
                    Id = "village",
                    Name = "Village",
                    PvpAllowed = pvp,
                },
                Zones = [.. areas],
            }
        );

    [Fact]
    public void ASafeArea_IsSafeAndRefusesPvp()
    {
        CompiledZone zone = Zone(true, Area("inn", "Safe", "Auberge", 0, 2));

        (zone.SafeAt(1, 1), zone.PvpAt(1, 1)).Should().Be((true, false));
        (zone.SafeAt(4, 1), zone.PvpAt(4, 1)).Should().Be((false, true));
    }

    [Fact]
    public void APvpArea_AllowsPvpInAPeacefulZone()
    {
        CompiledZone zone = Zone(false, Area("arena", "PvP", "Arène", 0, 2));

        zone.PvpAt(1, 1).Should().BeTrue();
        zone.PvpAt(4, 1).Should().BeFalse();
    }

    [Fact]
    public void AnotherKind_FollowsTheZone()
    {
        CompiledZone zone = Zone(true, Area("town", "Town", "Ville", 0, 5));

        (zone.SafeAt(1, 1), zone.PvpAt(1, 1)).Should().Be((false, true));
    }

    [Fact]
    public void TheHigherPriority_Decides()
    {
        CompiledZone zone = Zone(
            false,
            Area("arena", "PvP", "Arène", 0, 5, priority: 1),
            Area("shelter", "Safe", "Abri", 0, 2, priority: 2)
        );

        zone.SafeAt(1, 1).Should().BeTrue();
        zone.PvpAt(1, 1).Should().BeFalse();
        zone.PvpAt(4, 1).Should().BeTrue();
    }
}
