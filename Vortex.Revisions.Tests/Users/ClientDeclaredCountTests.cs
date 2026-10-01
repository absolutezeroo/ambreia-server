using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.FriendList;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
/// A count the client declares sizes nothing on its own. <c>new List&lt;int&gt;(PopInt())</c> let one
/// packet ask for 8 GB before reading its first id; <see cref="IClientPacket.PopCount"/> clamps the
/// count to what the bytes left could hold.
/// </summary>
public sealed class ClientDeclaredCountTests
{
    private const int AcceptFriendMessageEvent = 1772;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void ACountThePayloadCannotBack_ReadsOnlyWhatIsThere()
    {
        ServerPacket sp = new(AcceptFriendMessageEvent);
        sp.WriteInteger(int.MaxValue);
        sp.WriteInteger(42);

        AcceptFriendMessage message = Revision
            .Parsers[AcceptFriendMessageEvent]
            .Parse(new ClientPacket(AcceptFriendMessageEvent, sp.ToArray()))
            .Should()
            .BeOfType<AcceptFriendMessage>()
            .Subject;

        message.Friends.Should().Equal(42);
    }

    [Fact]
    public void ANegativeCount_IsAnEmptyList()
    {
        ServerPacket sp = new(AcceptFriendMessageEvent);
        sp.WriteInteger(-5);

        AcceptFriendMessage message = Revision
            .Parsers[AcceptFriendMessageEvent]
            .Parse(new ClientPacket(AcceptFriendMessageEvent, sp.ToArray()))
            .Should()
            .BeOfType<AcceptFriendMessage>()
            .Subject;

        message.Friends.Should().BeEmpty();
    }

    /// <summary>The goodbye a replaced session gets: one int, which the client turns into
    /// <c>${disconnected.concurrent_login}</c> for 2.</summary>
    [Fact]
    public void DisconnectReason_IsTheOneIntTheClientReads()
    {
        byte[] bytes = Revision
            .Serializers[typeof(DisconnectReasonEventMessageComposer)]
            .Serialize(
                new DisconnectReasonEventMessageComposer
                {
                    Reason = DisconnectReasonEventMessageComposer.ConcurrentLogin,
                }
            )
            .ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        ClientPacket packet = new(0, body);

        packet.PopInt().Should().Be(2);
        packet.Remaining.Should().Be(0);
    }
}
