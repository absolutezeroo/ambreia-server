using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>The player died (spec M1a §8.1, 9015): a ghost until he respawns, in this many seconds.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgDeathMessageComposer : IComposer
{
    [Id(0)]
    public required int RespawnSeconds { get; init; }
}
