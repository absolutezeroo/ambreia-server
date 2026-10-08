using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;

namespace Vortex.Rpg.Characters.Stats;

/// <summary>The worn items (spec Objets §8): their stats summed, as the character grain hands
/// them in.</summary>
public sealed class EquipmentStatSource : IStatSource
{
    public string Name => "equipment";

    public ValueTask<StatBlock> ContributeAsync(StatSourceContext context, CancellationToken ct)
    {
        IReadOnlyList<StatBlock> worn = context.Equipment;

        return ValueTask.FromResult(
            new StatBlock
            {
                Strength = worn.Sum(s => s.Strength),
                Intelligence = worn.Sum(s => s.Intelligence),
                Agility = worn.Sum(s => s.Agility),
                Health = worn.Sum(s => s.Health),
                Mana = worn.Sum(s => s.Mana),
                Armor = worn.Sum(s => s.Armor),
                Speed = worn.Sum(s => s.Speed),
            }
        );
    }
}
