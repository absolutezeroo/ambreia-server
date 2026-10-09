using FluentAssertions;
using Vortex.Rpg.Combat.Combatants;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

/// <summary>Spec M1a §6.3: a monster aims at the top of its threat table.</summary>
public sealed class ThreatTableTests
{
    [Fact]
    public void TheTop_IsTheMostThreat()
    {
        ThreatTable table = new();

        table.Add(1, 10);
        table.Add(2, 30);
        table.Add(1, 5);

        table.Top.Should().Be(2);
        table.Contains(1).Should().BeTrue();
    }

    [Fact]
    public void Removing_MovesTheTop()
    {
        ThreatTable table = new();
        table.Add(1, 10);
        table.Add(2, 30);

        table.Remove(2);

        table.Top.Should().Be(1);
    }

    [Fact]
    public void Ties_KeepTheFirstComer()
    {
        ThreatTable table = new();
        table.Add(7, 10);
        table.Add(3, 10);

        table.Top.Should().Be(7);
    }

    [Fact]
    public void AnEmptyTable_HasNoTop()
    {
        ThreatTable table = new();
        table.Add(1, 10);

        table.Clear();

        (table.Top, table.IsEmpty).Should().Be((null, true));
    }
}
