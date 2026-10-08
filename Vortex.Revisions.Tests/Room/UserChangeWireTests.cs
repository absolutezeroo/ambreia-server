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
///     UserChange carries five fields, and the client reads exactly those five. A block one field
///     short or long does not degrade, it kills the packet (<c>End of buffer</c>, or leftovers).
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
    public void Payload_IsTheFiveFieldsTheClientReads()
    {
        ClientPacket body = Serialize(NewChange());

        body.PopInt().Should().Be(7);
        body.PopString().Should().Be("hd-180-1");
        body.PopString().Should().Be("M");
        body.PopString().Should().Be("hi");
        body.PopInt().Should().Be(42);

        body.Remaining.Should().Be(0, "the badge rank and the two discarded fields are gone");
    }
}
