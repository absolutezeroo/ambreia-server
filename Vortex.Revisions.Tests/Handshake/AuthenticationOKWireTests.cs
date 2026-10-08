using System;
using FluentAssertions;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Handshake;

/// <summary>
/// AuthenticationOK is the account id and the identity id, nothing else: the suggested login
/// actions Habbo put between them were always empty here and left the wire on both sides.
/// </summary>
public sealed class AuthenticationOKWireTests
{
    private static readonly Rev Revision = new();

    [Fact]
    public void Payload_IsTheAccountIdThenTheIdentityId()
    {
        byte[] bytes = Revision
            .Serializers[typeof(AuthenticationOKMessage)]
            .Serialize(new AuthenticationOKMessage { AccountId = 7, IdentityId = 9 })
            .ToArray();
        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        ClientPacket body = new(0, payload);

        body.PopInt().Should().Be(7);
        body.PopInt().Should().Be(9);
        body.Remaining.Should().Be(0);
    }
}
