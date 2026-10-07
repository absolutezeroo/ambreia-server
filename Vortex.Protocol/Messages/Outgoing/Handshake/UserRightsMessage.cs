using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

[GenerateSerializer, Immutable]
public sealed record UserRightsMessage : IComposer
{
    [Id(0)]
    public required MembershipLevelType MembershipLevel { get; init; }

    [Id(1)]
    public required SecurityLevelType SecurityLevel { get; init; }

    [Id(2)]
    public required bool IsAmbassador { get; init; }
}
