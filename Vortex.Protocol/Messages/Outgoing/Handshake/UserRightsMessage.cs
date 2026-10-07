using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

public sealed record UserRightsMessage : IComposer
{
    public required MembershipLevelType MembershipLevel { get; init; }
    public required SecurityLevelType SecurityLevel { get; init; }
    public required bool IsAmbassador { get; init; }
}
