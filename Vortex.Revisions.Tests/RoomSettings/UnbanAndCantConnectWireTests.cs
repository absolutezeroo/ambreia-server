using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.RoomSettings;

/// <summary>
/// Two answers the room settings dialog and the navigator read, both of which used to be written
/// wrong or not at all.
/// </summary>
/// <remarks>
/// <list type="bullet">
///     <item>
///         <c>UserUnbannedFromRoom</c> (2089) had an empty serializer and no sender.
///         <c>RoomSettingsCtrl.onUserUnbannedFromRoom</c> is the only thing that takes a row out of
///         the ban list, so lifting a ban left the name on screen.
///         <c>unknowns/_SafePkg_2452/_SafeCls_3788.parse</c> reads the room then the user.
///     </item>
///     <item>
///         <c>CantConnect</c> carried its string parameter under <c>Banned</c>.
///         <c>unknowns/_SafePkg_1744/_SafeCls_2738.parse</c> reads it only when the reason is 3
///         (<c>EnterQueue</c>), and builds "${room.queue.error.&lt;parameter&gt;}" from it.
///     </item>
/// </list>
/// </remarks>
public sealed class UnbanAndCantConnectWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void UserUnbanned_IsTheRoomThenThePlayer()
    {
        ClientPacket packet = Body(
            typeof(UserUnbannedFromRoomEventMessageComposer),
            new UserUnbannedFromRoomEventMessageComposer { RoomId = 4271, PlayerId = 77 }
        );

        packet.PopInt().Should().Be(4271);
        packet.PopInt().Should().Be(77);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void CantConnect_WritesTheParameterOnlyForTheQueueReason()
    {
        ClientPacket queue = Body(
            typeof(CantConnectMessageComposer),
            new CantConnectMessageComposer
            {
                ErrorType = RoomConnectionErrorType.EnterQueue,
                AdditionalInfo = "full",
            }
        );

        queue.PopInt().Should().Be(3);
        queue.PopString().Should().Be("full");
        queue.Remaining.Should().Be(0);
    }

    [Fact]
    public void CantConnect_LeavesABanAsTheReasonAlone()
    {
        // The client stops reading after the reason for anything but 3, so the string written here
        // was bytes nobody looked at -- and the same code would have under-written an EnterQueue.
        ClientPacket banned = Body(
            typeof(CantConnectMessageComposer),
            new CantConnectMessageComposer
            {
                ErrorType = RoomConnectionErrorType.Banned,
                AdditionalInfo = "ignored",
            }
        );

        banned.PopInt().Should().Be(4);
        banned.Remaining.Should().Be(0);
    }

    private static ClientPacket Body(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }
}
