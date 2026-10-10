using System.Collections.Generic;

namespace Vortex.Rpg.Content.Rules;

/// <summary>The keys of the monster behaviours the modules declare: the import refuses a monster whose
/// behaviour nobody declares (spec M1c §5.4). The monsters module answers from every declared
/// behaviour, so declaring one is enough.</summary>
public interface IMonsterBehaviorKeys
{
    public IReadOnlySet<string> Keys { get; }
}
