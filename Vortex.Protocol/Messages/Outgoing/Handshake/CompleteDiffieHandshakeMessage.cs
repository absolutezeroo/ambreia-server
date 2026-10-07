using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

[GenerateSerializer, Immutable]
public sealed record CompleteDiffieHandshakeMessageComposer : IComposer
{
    [Id(0)]
    public required string PublicKey { get; init; }

    [Id(1)]
    public bool ServerClientEncryption { get; init; }
}
