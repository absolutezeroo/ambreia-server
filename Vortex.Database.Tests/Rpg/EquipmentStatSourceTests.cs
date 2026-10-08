using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec Objets §8: the stats of the worn items, summed.</summary>
public sealed class EquipmentStatSourceTests
{
    [Fact]
    public async Task TheEquipment_IsSummed()
    {
        StatSourceContext context = new(new PlayerId(7), null, 1)
        {
            Equipment =
            [
                new StatBlock { Strength = 3 },
                new StatBlock { Agility = 1, Strength = 2 },
            ],
        };

        StatBlock stats = await new EquipmentStatSource().ContributeAsync(
            context,
            CancellationToken.None
        );

        stats.Should().Be(new StatBlock { Strength = 5, Agility = 1 });
    }

    [Fact]
    public async Task NoEquipment_IsZero()
    {
        StatBlock stats = await new EquipmentStatSource().ContributeAsync(
            new StatSourceContext(new PlayerId(7), null, 1),
            CancellationToken.None
        );

        stats.Should().Be(new StatBlock());
        new EquipmentStatSource().Name.Should().Be("equipment");
    }
}
