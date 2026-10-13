using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>An attack the player asked for was refused (spec M1a §9.2, 9016), to him alone.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCombatResultMessageComposer : IComposer
{
    /// <summary>The player or the target stands on a safe cell.</summary>
    public const string SafeZone = "safe_zone";

    /// <summary>No path leads to the target.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>The target or the player is dead.</summary>
    public const string Dead = "dead";

    /// <summary>L'attaque auto sans monstre vivant sélectionné (M1d §2.3).</summary>
    public const string InvalidTarget = "invalid_target";

    [Id(0)]
    public required string Code { get; init; }
}
