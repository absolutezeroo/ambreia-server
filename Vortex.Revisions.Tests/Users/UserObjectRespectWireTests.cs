using System;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
/// The user object's respect block, which used to be three hard-coded zeroes.
/// </summary>
/// <remarks>
/// Those zeroes switched the whole feature off in the client rather than merely reporting nothing.
/// <c>SessionDataManager.onUserObject</c> is the only writer of <c>respectLeft</c> and
/// <c>petRespectLeft</c>; <c>AvatarMenuView</c> draws the respect button only while the first is
/// above zero and <c>OwnPetMenuView</c> the scratch button only while the second is. A complete
/// server-side respect system sat behind a button that was never rendered.
/// <para>
/// The two trailing pairs are read behind separate <c>bytesAvailable &gt; 0</c> guards in
/// <c>unknowns/_SafePkg_1820/_SafeCls_1819.parse</c>. The second is unreachable without the first,
/// which is why the trade lock and the name colour are written even though neither is used here.
/// </para>
/// </remarks>
public sealed class UserObjectRespectWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void TheRespectBlock_CarriesTheRealCountersAndTheTwoOptionalPairs()
    {
        ClientPacket packet = Body(
            new UserObjectMessage
            {
                Player = NewPlayer(),
                Respect = new PlayerRespectSnapshot
                {
                    Received = 42,
                    Left = 2,
                    PetLeft = 3,
                    ReplenishesLeft = 1,
                    MaxPerDay = 3,
                },
            }
        );

        packet.PopInt().Should().Be(7, "the player id comes first");
        packet.PopString().Should().Be("Tester");
        packet.PopString().Should().Be("hd-180-1");
        packet.PopString().Should().NotBeNull("gender");
        packet.PopString().Should().Be("motto");
        packet.PopString().Should().Be("Tester", "real name repeats the name");
        packet.PopBoolean().Should().BeFalse("direct mail");

        packet.PopInt().Should().Be(42, "respectTotal");
        packet.PopInt().Should().Be(2, "respectLeft -- the respect button's only condition");
        packet.PopInt().Should().Be(3, "petRespectLeft -- the scratch button's only condition");

        packet.PopBoolean().Should().BeFalse("stream publishing");
        packet
            .PopString()
            .Should()
            .Be("01-01-1970 00:00:00", "lastAccessDate carries the official server's fixed format");
        packet.PopBoolean().Should().BeFalse("name change allowed");
        packet.PopBoolean().Should().BeFalse("account safety locked");

        packet.PopBoolean().Should().BeFalse("account trade locked -- first optional pair");
        packet.PopString().Should().BeEmpty("name colour -- first optional pair");
        packet.PopInt().Should().Be(1, "respectReplenishesLeft -- second optional pair");
        packet.PopInt().Should().Be(3, "maxRespectPerDay -- second optional pair");

        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void AnExhaustedBudget_IsStillWrittenAsZeroRatherThanOmitted()
    {
        // Nothing here is conditional on the server side: the client reads all fourteen required
        // fields whatever they hold, and a shortened body would misread the ones after it.
        ClientPacket packet = Body(
            new UserObjectMessage
            {
                Player = NewPlayer(),
                Respect = new PlayerRespectSnapshot
                {
                    Received = 0,
                    Left = 0,
                    PetLeft = 0,
                    ReplenishesLeft = 0,
                    MaxPerDay = 3,
                },
            }
        );

        SkipIdentity(packet);

        packet.PopInt().Should().Be(0, "respectTotal");
        packet.PopInt().Should().Be(0, "respectLeft");
        packet.PopInt().Should().Be(0, "petRespectLeft");

        packet.PopBoolean();
        packet.PopString();
        packet.PopBoolean();
        packet.PopBoolean();

        packet.PopBoolean();
        packet.PopString();
        packet.PopInt().Should().Be(0, "respectReplenishesLeft");
        packet
            .PopInt()
            .Should()
            .Be(3, "the client defaults this to 3 on its own, but it is still a server number");
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

            ClientPacket packet = Body(
                new UserObjectMessage { Player = NewPlayer(), Respect = NoRespect }
            );

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

    private static PlayerRespectSnapshot NoRespect =>
        new()
        {
            Received = 0,
            Left = 0,
            PetLeft = 0,
            ReplenishesLeft = 0,
            MaxPerDay = 3,
        };

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
