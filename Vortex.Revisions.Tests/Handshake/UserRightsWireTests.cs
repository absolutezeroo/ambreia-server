using System;
using FluentAssertions;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players.Enums;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Handshake;

/// <summary>
/// UserRights is the security level alone: Habbo's club level before it and ambassador flag after
/// it were constants here (no club, no ambassadors) and left the wire on both sides.
/// </summary>
public sealed class UserRightsWireTests
{
    private static readonly Rev Revision = new();

    [Fact]
    public void Payload_IsTheSecurityLevelAlone()
    {
        byte[] bytes = Revision
            .Serializers[typeof(UserRightsMessage)]
            .Serialize(new UserRightsMessage { SecurityLevel = SecurityLevelType.Moderator })
            .ToArray();
        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        ClientPacket body = new(0, payload);

        body.PopInt().Should().Be((int)SecurityLevelType.Moderator);
        body.Remaining.Should().Be(0);
    }
}
