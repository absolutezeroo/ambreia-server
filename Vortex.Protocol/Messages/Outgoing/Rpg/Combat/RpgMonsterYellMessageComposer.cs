using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>A monster shouts a line (spec M1c §9.2, 9026), to the zone: a key of the content's texts.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgMonsterYellMessageComposer : IComposer
{
    [Id(0)]
    public required int ObjectId { get; init; }

    [Id(1)]
    public required string TextKey { get; init; }
}
