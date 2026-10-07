using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Players;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

public sealed record UserObjectMessage : IComposer
{
    public required PlayerSummarySnapshot Player { get; init; }
}
