using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

[GenerateSerializer, Immutable]
public sealed record AuthenticationOKMessage : IComposer
{
    [Id(0)]
    public required int AccountId { get; init; }

    // [Id(1)] retired: SuggestedLoginActions, always empty and no longer on the wire.

    [Id(2)]
    public required int IdentityId { get; init; }
}
