using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
///     The client reads nine fields from UserChange and the serializer used to write five, so every
///     read past the achievement score ran off the end of the buffer. The client threw
///     <c>End of buffer</c> and dropped the message whole — a figure or motto changed outside the
///     room never reached anyone standing in it.
///
///     Same shape as the missing badge rank in <see cref="RoomAvatarWireTests" />: a block one field
///     short does not degrade, it kills the packet.
/// </summary>
public sealed class UserChangeWireTests
{
    private static readonly Rev Revision = new();

    /// <summary>Goes through the registered composer, so the test sees exactly what a connected
    /// client would receive.</summary>
    private static ClientPacket Serialize(UserChangeMessageComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(UserChangeMessageComposer)]
            .Serialize(composer)
            .ToArray();

        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }

    private static UserChangeMessageComposer NewChange() =>
        new()
        {
            ObjectId = 7,
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            CustomInfo = "hi",
            AchievementScore = 42,
        };

    [Fact]
    public void Payload_CarriesEveryFieldTheClientReads()
    {
        ClientPacket body = Serialize(NewChange());

        body.PopInt().Should().Be(7);
        body.PopString().Should().Be("hd-180-1");
        body.PopString().Should().Be("M");
        body.PopString().Should().Be("hi");
        body.PopInt().Should().Be(42);

        // The three the client reads and discards. They are what used to be missing.
        body.PopString().Should().BeEmpty();
        body.PopInt()
            .Should()
            .Be(0, "the triplet list is empty, so the client's loop runs zero times");
        body.PopInt()
            .Should()
            .Be(
                -1,
                "no player has a badge rank yet, and -1 is how the client is told so — it draws the "
                    + "rank row for anything >= 0 (infostand/InfoStandUserView.as:638-646)"
            );
    }

    /// <summary>
    ///     The point of the fix: the client's read order has to consume the payload exactly, with
    ///     nothing left over and nothing missing.
    /// </summary>
    [Fact]
    public void Payload_IsFullyConsumedByTheClientReadOrder()
    {
        ClientPacket body = Serialize(NewChange());

        body.PopInt();
        body.PopString();
        body.PopString();
        body.PopString();
        body.PopInt();
        body.PopString();

        int triplets = body.PopInt();

        for (int i = 0; i < triplets; i++)
        {
            body.PopInt();
            body.PopInt();
            body.PopInt();
        }

        body.PopInt();

        body.Remaining.Should().Be(0);
    }
}
