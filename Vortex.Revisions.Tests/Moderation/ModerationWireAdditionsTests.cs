using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Moderator;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Moderation;

/// <summary>
///     Wire contract for the room tool's caution/message broadcast, a mod-tool feature that had no
///     server side at all. It was decoded from the WIN63-202607011411 client rather than guessed, so
///     these tests pin the layout that reading was based on.
/// </summary>
public sealed class ModerationWireAdditionsTests
{
    private const int ModToolRoomAlertMessageEvent = 2735;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void RoomAlert_ReadsActionTypeAsAFourByteIntAndConsumesTheTrailingString()
    {
        // _SafeCls_3239(param1:int, param2:String, param3:String) -- param1 is a real AS3 int, so it
        // is four bytes and not a one-byte boolean.
        ServerPacket sp = new(ModToolRoomAlertMessageEvent);
        sp.WriteInteger(4).WriteString("clean this up").WriteString(string.Empty);

        ClientPacket packet = new(ModToolRoomAlertMessageEvent, sp.ToArray());

        ModToolRoomAlertMessage message = (ModToolRoomAlertMessage)
            Revision.Parsers[ModToolRoomAlertMessageEvent].Parse(packet);

        message.ActionType.Should().Be(4);
        message.Message.Should().Be("clean this up");

        // Everything after the message must have been consumed, or the next packet in the buffer
        // starts mid-string.
        packet.Remaining.Should().Be(0);
    }

    [Theory]
    // determineAction(isCaution, kickUsers): 0/1 are the caution pair, 3/4 the message pair.
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void RoomAlert_SplitsCautionFromMessageOnTheClientsOwnConstants(
        int actionType,
        bool expectedCaution
    )
    {
        new ModToolRoomAlertMessage { ActionType = actionType, Message = "x" }
            .IsCaution.Should()
            .Be(expectedCaution);
    }

    [Fact]
    public void RoomAlert_HasARegisteredParser()
    {
        Revision.Parsers.Should().ContainKey(ModToolRoomAlertMessageEvent);
    }
}
