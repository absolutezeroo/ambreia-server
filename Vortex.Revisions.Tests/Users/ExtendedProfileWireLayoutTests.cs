using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
///     Locks the extended-profile byte contract against the Flash client.
///
///     Two things were wrong before these tests existed. The composer stopped after
///     <c>BooleanField27</c> where WIN63's parser
///     (unknowns/_SafePkg_1731/_SafeCls_2228.as) reads four more values — total badges,
///     achievement level, a counted list of (rarity, count) pairs and the badge rank — so the
///     client ran off the end of the packet rather than merely missing a panel. And the online
///     flag was a <c>bool</c> where the client reads a byte with three states, which made
///     "online but hidden" unsendable.
/// </summary>
public sealed class ExtendedProfileWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket SerializeAndReadBody(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    private static ExtendedProfileMessageComposer Profile(
        int onlineStatus,
        List<BadgeRarityCount>? rarities = null
    ) =>
        new()
        {
            UserId = 4711,
            UserName = "Frank",
            Figure = "hd-180-1",
            Motto = "hi",
            CreationDate = "01-01-2020",
            AchievementScore = 12,
            FriendCount = 3,
            IsFriend = true,
            IsFriendRequestSent = false,
            OnlineStatus = onlineStatus,
            Guilds = [],
            LastAccessSinceInSeconds = 60,
            OpenProfileWindow = true,
            IsHidden = onlineStatus == OnlineStatusCodes.Hidden,
            IntegerField24 = 0,
            StarGemCount = 7,
            BooleanField26 = false,
            BooleanField27 = true,
            TotalBadges = 42,
            AchievementLevel = 9,
            BadgeRarityCounts = rarities ?? [],
            TotalBadgesRank = 1337,
        };

    /// <summary>
    ///     Walks the whole packet in the client's read order. If any field is added, dropped or
    ///     reordered, the reads after it come back as garbage and this fails.
    /// </summary>
    [Fact]
    public void ExtendedProfile_MatchesTheClientReadOrder()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(ExtendedProfileMessageComposer),
            Profile(
                OnlineStatusCodes.Online,
                [
                    new BadgeRarityCount { RarityId = 2, Count = 5 },
                    new BadgeRarityCount { RarityId = 3, Count = 1 },
                ]
            )
        );

        packet.PopInt().Should().Be(4711);
        packet.PopString().Should().Be("Frank");
        packet.PopString().Should().Be("hd-180-1");
        packet.PopString().Should().Be("hi");
        packet.PopString().Should().Be("01-01-2020");
        packet.PopInt().Should().Be(12);
        packet.PopInt().Should().Be(3);
        packet.PopBoolean().Should().BeTrue();
        packet.PopBoolean().Should().BeFalse();
        packet.PopByte().Should().Be((byte)OnlineStatusCodes.Online);
        packet.PopInt().Should().Be(0); // guild count
        packet.PopInt().Should().Be(60);
        packet.PopBoolean().Should().BeTrue();
        packet.PopBoolean().Should().BeFalse(); // isHidden
        packet.PopInt().Should().Be(0); // accountLevel, frozen
        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(7);
        packet.PopBoolean().Should().BeFalse();
        packet.PopBoolean().Should().BeTrue();

        // The four the composer used to stop short of.
        packet.PopInt().Should().Be(42); // totalBadges
        packet.PopInt().Should().Be(9); // achievementLevel

        packet.PopInt().Should().Be(2); // badgeRarityCounts length
        packet.PopByte().Should().Be(2);
        packet.PopInt().Should().Be(5);
        packet.PopByte().Should().Be(3);
        packet.PopInt().Should().Be(1);

        packet.PopInt().Should().Be(1337); // totalBadgesRank
    }

    /// <summary>
    ///     The state a <c>bool</c> could not carry. A hidden player is online *and* hidden, and the
    ///     client picks its third status icon off this single byte.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OnlineStatus_SurvivesAllThreeStates(int status)
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(ExtendedProfileMessageComposer),
            Profile(status)
        );

        for (int i = 0; i < 5; i++)
        {
            if (i == 0)
            {
                packet.PopInt();
                continue;
            }

            packet.PopString();
        }

        packet.PopInt();
        packet.PopInt();
        packet.PopBoolean();
        packet.PopBoolean();

        packet.PopByte().Should().Be((byte)status);
    }

    /// <summary>
    ///     An empty rarity list still writes its length. Dropping the zero would shift every
    ///     following read by four bytes, which is the failure mode these tests exist to catch.
    /// </summary>
    [Fact]
    public void EmptyBadgeRarityCounts_StillWritesItsLength()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(ExtendedProfileMessageComposer),
            Profile(OnlineStatusCodes.Offline)
        );

        packet.PopInt();

        for (int i = 0; i < 4; i++)
        {
            packet.PopString();
        }

        packet.PopInt();
        packet.PopInt();
        packet.PopBoolean();
        packet.PopBoolean();
        packet.PopByte();
        packet.PopInt();
        packet.PopInt();
        packet.PopBoolean();
        packet.PopBoolean();
        packet.PopInt();
        packet.PopInt();
        packet.PopInt();
        packet.PopBoolean();
        packet.PopBoolean();
        packet.PopInt();
        packet.PopInt();

        packet.PopInt().Should().Be(0); // the length, present even when the list is empty
        packet.PopInt().Should().Be(1337); // and the rank still lands where the client expects it
    }
}
