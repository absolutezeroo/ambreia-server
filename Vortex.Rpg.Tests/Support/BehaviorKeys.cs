using System.Collections.Generic;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Tests.Support;

/// <summary>The behaviours modules would declare, by their keys only.</summary>
internal sealed class BehaviorKeys(params string[] keys) : IMonsterBehaviorKeys
{
    public IReadOnlySet<string> Keys { get; } = new HashSet<string>(keys);
}
