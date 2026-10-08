using System.Threading;
using System.Threading.Tasks;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;

namespace Vortex.Rpg.Characters.Stats;

/// <summary>The class at its level: <c>base + perLevel × (level − 1)</c> (spec Personnage §6.1).</summary>
public sealed class ClassLevelStatSource : IStatSource
{
    public string Name => "class";

    public ValueTask<StatBlock> ContributeAsync(StatSourceContext context, CancellationToken ct)
    {
        if (context.Class is not { } c)
        {
            return ValueTask.FromResult(new StatBlock());
        }

        int levels = context.Level - 1;

        return ValueTask.FromResult(
            new StatBlock
            {
                Strength = c.Base.Strength + c.PerLevel.Strength * levels,
                Intelligence = c.Base.Intelligence + c.PerLevel.Intelligence * levels,
                Agility = c.Base.Agility + c.PerLevel.Agility * levels,
                Health = c.Base.Health + c.PerLevel.Health * levels,
                Mana = c.Base.Mana + c.PerLevel.Mana * levels,
                Armor = c.Base.Armor + c.PerLevel.Armor * levels,
                Speed = c.Base.Speed + c.PerLevel.Speed * levels,
            }
        );
    }
}
