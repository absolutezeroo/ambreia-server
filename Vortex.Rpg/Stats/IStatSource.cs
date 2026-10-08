using System.Threading;
using System.Threading.Tasks;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Stats;

/// <summary>
/// One contribution to a character's stats (spec Personnage §6.1): the class and level, the
/// equipment. Declared in the container by the module that owns it
/// (<c>services.AddSingleton&lt;IStatSource, ClassLevelStatSource&gt;()</c>), like content kinds.
/// </summary>
public interface IStatSource
{
    /// <summary>The name shown in the stats detail (<c>class</c>, <c>equipment</c>).</summary>
    public string Name { get; }

    public ValueTask<StatBlock> ContributeAsync(StatSourceContext context, CancellationToken ct);
}
