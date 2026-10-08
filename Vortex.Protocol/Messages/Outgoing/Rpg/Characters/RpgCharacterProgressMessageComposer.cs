using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>XP and level after a gain (spec F3a §6.2, 9004).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCharacterProgressMessageComposer : IComposer
{
    [Id(0)]
    public required int Level { get; init; }

    [Id(1)]
    public required int Xp { get; init; }

    /// <summary>0 at the max level.</summary>
    [Id(2)]
    public required int XpToNext { get; init; }
}
