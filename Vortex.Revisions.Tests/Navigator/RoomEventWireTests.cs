using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Navigator;

/// <summary>
/// The room advertisement carried five values and the client reads ten.
/// </summary>
/// <remarks>
/// <para>
/// Layout from the client's own parser — <c>RoomEventData</c>'s constructor, which consumes ten
/// values back to back: the advertisement id, the owner's id and name, the room id, an event type,
/// the name, the description, the age in minutes, the minutes left, and the category. There is no
/// bytes-available guard anywhere in it, so every field is mandatory.
/// </para>
/// <para>
/// What the short version did is worth naming, because it did not look like a truncation from the
/// server side: the client took the room id for the advertisement id, the name's length prefix for
/// an owner id, and ran off the end of the buffer somewhere inside the description. The ad copy the
/// player had just typed came back as nothing at all.
/// </para>
/// </remarks>
public sealed class RoomEventWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void WritesTheTenValuesTheClientParserReads()
    {
        ClientPacket body = Serialize(
            new RoomEventMessageComposer
            {
                AdvertisementId = 88,
                OwnerPlayerId = 7,
                OwnerName = "Akikazu",
                RoomId = 47006506,
                Name = "Soiree",
                Description = "venez nombreux",
                MinutesSinceCreation = 12,
                MinutesRemaining = 108,
                CategoryId = 3,
            }
        );

        body.PopInt().Should().Be(88);
        body.PopInt().Should().Be(7);
        body.PopString().Should().Be("Akikazu");
        body.PopInt().Should().Be(47006506);
        body.PopInt().Should().Be(0); // eventType, unknown and unread by the client
        body.PopString().Should().Be("Soiree");
        body.PopString().Should().Be("venez nombreux");
        body.PopInt().Should().Be(12);
        body.PopInt().Should().Be(108);
        body.PopInt().Should().Be(3);

        body.Remaining.Should()
            .Be(0, "the parser stops after the category and expects nothing more");
    }

    /// <summary>
    /// The advertisement id is not the room id. They were the same field once, which is why a
    /// distinct value for each is what this asserts.
    /// </summary>
    [Fact]
    public void TheAdvertisementIdLeadsAndIsNotTheRoomId()
    {
        ClientPacket body = Serialize(
            new RoomEventMessageComposer
            {
                AdvertisementId = 1,
                OwnerPlayerId = 7,
                OwnerName = "Akikazu",
                RoomId = 999,
                Name = "Soiree",
                Description = null,
                MinutesSinceCreation = 0,
                MinutesRemaining = 60,
                CategoryId = 0,
            }
        );

        body.PopInt().Should().Be(1);
        body.PopInt().Should().Be(7);
        body.PopString().Should().Be("Akikazu");
        body.PopInt().Should().Be(999);
    }

    /// <summary>A null description is an empty string on the wire, never a skipped field.</summary>
    [Fact]
    public void ANullDescriptionStillOccupiesItsField()
    {
        ClientPacket body = Serialize(
            new RoomEventMessageComposer
            {
                AdvertisementId = 1,
                OwnerPlayerId = 7,
                OwnerName = string.Empty,
                RoomId = 999,
                Name = "Soiree",
                Description = null,
                MinutesSinceCreation = 0,
                MinutesRemaining = 60,
                CategoryId = 4,
            }
        );

        body.PopInt();
        body.PopInt();
        body.PopString();
        body.PopInt();
        body.PopInt();
        body.PopString().Should().Be("Soiree");
        body.PopString().Should().BeEmpty();
        body.PopInt().Should().Be(0);
        body.PopInt().Should().Be(60);
        body.PopInt().Should().Be(4);
    }

    private static ClientPacket Serialize(RoomEventMessageComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(RoomEventMessageComposer)]
            .Serialize(composer)
            .ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }
}
