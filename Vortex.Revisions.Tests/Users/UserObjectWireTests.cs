using System;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
/// The user object's layout. Respects were removed (M0 bis) but the client still reads their five
/// counters, so they stay on the wire frozen at zero -- which is also what keeps the client's
/// respect and scratch buttons hidden.
/// </summary>
/// <remarks>
/// The two trailing pairs are read behind separate <c>bytesAvailable &gt; 0</c> guards in
/// <c>unknowns/_SafePkg_1820/_SafeCls_1819.parse</c>. The second is unreachable without the first,
/// which is why the trade lock and the name colour are written even though neither is used here.
/// </remarks>
public sealed class UserObjectWireTests
{
    private static readonly Rev Revision = new();

    [Fact]
    public void TheFrozenRespectBlock_IsWrittenAsZeroesInTheSameShape()
    {
        ClientPacket packet = Body(new UserObjectMessage { Player = NewPlayer() });

        packet.PopInt().Should().Be(7, "the player id comes first");
        packet.PopString().Should().Be("Tester");
        packet.PopString().Should().Be("hd-180-1");
        packet.PopString().Should().NotBeNull("gender");
        packet.PopString().Should().Be("motto");
        packet.PopString().Should().Be("Tester", "real name repeats the name");
        packet.PopBoolean().Should().BeFalse("direct mail");

        packet.PopInt().Should().Be(0, "respectTotal, frozen");
        packet.PopInt().Should().Be(0, "respectLeft, frozen");
        packet.PopInt().Should().Be(0, "petRespectLeft, frozen");

        packet.PopBoolean().Should().BeFalse("stream publishing");
        packet
            .PopString()
            .Should()
            .Be("01-01-1970 00:00:00", "lastAccessDate carries the official server's fixed format");
        packet.PopBoolean().Should().BeFalse("name change allowed");
        packet.PopBoolean().Should().BeFalse("account safety locked");

        packet.PopBoolean().Should().BeFalse("account trade locked -- first optional pair");
        packet.PopString().Should().BeEmpty("name colour -- first optional pair");
        packet.PopInt().Should().Be(0, "respectReplenishesLeft, frozen -- second optional pair");
        packet.PopInt().Should().Be(0, "maxRespectPerDay, frozen -- second optional pair");

        packet.Remaining.Should().Be(0);
    }

    /// <summary>
    /// The date field used to go out through <c>CultureInfo.CurrentCulture</c>, which made the
    /// bytes a property of whatever locale the process happened to boot with. A dev box and a
    /// container running invariant sent two different packets from one build.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")] // a non-Gregorian default calendar, the case that shifts the year itself
    public void TheLastAccessDate_IsTheSameBytesWhateverTheHostLocaleIs(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            ClientPacket packet = Body(new UserObjectMessage { Player = NewPlayer() });

            SkipIdentity(packet);
            packet.PopInt();
            packet.PopInt();
            packet.PopInt();
            packet.PopBoolean();

            packet.PopString().Should().Be("01-01-1970 00:00:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The six identity fields and the direct-mail flag that precede the respect block.</summary>
    private static void SkipIdentity(ClientPacket packet)
    {
        packet.PopInt();

        for (int i = 0; i < 5; i++)
        {
            packet.PopString();
        }

        packet.PopBoolean();
    }

    private static PlayerSummarySnapshot NewPlayer() =>
        new()
        {
            PlayerId = new Primitives.Players.PlayerId(7),
            Name = "Tester",
            Motto = "motto",
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            AchievementScore = 0,
            CreatedAt = DateTime.UnixEpoch,
        };

    private static ClientPacket Body(IComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(UserObjectMessage)]
            .Serialize(composer)
            .ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }
}
