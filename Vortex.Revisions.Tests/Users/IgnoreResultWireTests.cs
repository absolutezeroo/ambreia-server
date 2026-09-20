using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
/// The ignore result's two integers, which used to go out in the wrong order.
/// </summary>
/// <remarks>
/// <c>IgnoreResultMessageEventParser.parse()</c> reads <c>result</c> first and
/// <c>ignoredUserId</c> second, and <c>IgnoredUsersManager.onIgnoreResult()</c> switches on the
/// first of the pair (1 ignored, 2 ignored-but-list-full, 3 unignored). Writing the player id
/// first made the client switch on the id: player 2 took the list-full arm and put player 1 on
/// the ignore list, player 3 took the unignore arm. Only player 1 survived the swap, which is
/// why it looked like it worked at all.
/// </remarks>
public sealed class IgnoreResultWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Theory]
    [InlineData(1)] // ignored
    [InlineData(2)] // ignored, oldest entry dropped
    [InlineData(3)] // unignored
    public void TheResultCode_ComesBeforeThePlayerId(int resultCode)
    {
        ClientPacket packet = Body(
            new IgnoreResultMessageComposer { UserId = 42, ResultCode = resultCode }
        );

        packet.PopInt().Should().Be(resultCode, "the client switches on the first integer");
        packet.PopInt().Should().Be(42, "the ignored player id is the second");
        packet.Remaining.Should().Be(0);
    }

    private static ClientPacket Body(IComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(IgnoreResultMessageComposer)]
            .Serialize(composer)
            .ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }
}
