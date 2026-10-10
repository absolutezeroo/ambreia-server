using Vortex.Rpg.Content.Rules;

namespace Vortex.Hosting.Tests.Rpg;

/// <summary>A behaviour the monsters module ships, by its key: what the repository's content may name.</summary>
internal sealed record ShippedBehaviorKey(string Key) : IMonsterBehaviorKey
{
    /// <summary>MonstersModule declares these: a new shipped behaviour is added here too.</summary>
    public static readonly IMonsterBehaviorKey[] All =
    [
        new ShippedBehaviorKey("melee"),
        new ShippedBehaviorKey("ranged"),
        new ShippedBehaviorKey("caster"),
    ];
}
