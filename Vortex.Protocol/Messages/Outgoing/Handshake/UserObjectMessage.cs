using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Players;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

[GenerateSerializer, Immutable]
public sealed record UserObjectMessage : IComposer
{
    [Id(0)]
    public required PlayerSummarySnapshot Player { get; init; }
}
