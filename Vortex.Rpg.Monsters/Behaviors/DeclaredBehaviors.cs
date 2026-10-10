using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>The content import's view of the behaviours: the key of every <see cref="IMonsterBehavior"/>
/// any module declared.</summary>
public sealed class DeclaredBehaviors(IEnumerable<IMonsterBehavior> behaviors)
    : IMonsterBehaviorKeys
{
    public IReadOnlySet<string> Keys { get; } = behaviors.Select(b => b.Key).ToFrozenSet();
}
