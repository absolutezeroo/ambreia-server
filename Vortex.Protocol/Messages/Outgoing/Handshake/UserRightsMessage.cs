using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

[GenerateSerializer, Immutable]
public sealed record UserRightsMessage : IComposer
{
    // [Id(0)] and [Id(2)] retired: the membership level and the ambassador flag, both constant
    // (no club, no ambassadors) and no longer on the wire.

    [Id(1)]
    public required SecurityLevelType SecurityLevel { get; init; }
}
