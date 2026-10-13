using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>Un soigneur propose de ranimer le joueur KO (M1d §2.3, 9029).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgResurrectOfferMessageComposer : IComposer
{
    [Id(0)]
    public required int CasterObjectId { get; init; }

    [Id(1)]
    public required int HealthPercent { get; init; }

    [Id(2)]
    public required int ExpiresMs { get; init; }
}
