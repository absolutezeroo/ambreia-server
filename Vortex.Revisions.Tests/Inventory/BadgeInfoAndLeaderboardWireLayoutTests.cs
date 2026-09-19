using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Badges;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Inventory;

/// <summary>
///     Locks the byte contract of the two badge messages the hotel had never sent.
///
///     Both are read by client classes that give up quietly rather than error: the badge display
///     widget drops an answer whose code is not the one it asked for, and the leaderboard drops the
///     whole message unless the chunk size reads back as exactly 50. A field written in the wrong
///     order therefore shows up as a plaque that never fills or a board that never loads, with
///     nothing in any log.
/// </summary>
public sealed class BadgeInfoAndLeaderboardWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket SerializeAndReadBody(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    /// <summary>unknowns/_SafePkg_3206/_SafeCls_4369.parse: badgeId, badgeCode, ownerCount, rarity.</summary>
    [Fact]
    public void BadgeInfo_WritesIdThenCodeThenCountThenRarity()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(BadgeInfoMessageComposer),
            new BadgeInfoMessageComposer
            {
                BadgeCode = "ACH_BasicClub1",
                OwnerCount = 42,
                BadgeRarityId = (int)BadgeRarityTier.Epic,
            }
        );

        packet.PopInt().Should().Be(0, "the client parses badgeId and no reader uses it");
        packet.PopString().Should().Be("ACH_BasicClub1");
        packet.PopInt().Should().Be(42);
        packet.PopInt().Should().Be(3, "Epic is the client's VERY_RARE = 3");
    }

    /// <summary>
    ///     unknowns/_SafePkg_1891/_SafeCls_3883.parse: type, rarity, page, size, totalEntries, a
    ///     counted block of rows, then a boolean gating one trailing row.
    /// </summary>
    [Fact]
    public void BadgeLeaderboardResult_WritesHeaderThenRowsThenOwnEntry()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(BadgeLeaderboardResultMessageComposer),
            new BadgeLeaderboardResultMessageComposer
            {
                Page = new BadgeLeaderboardPageSnapshot
                {
                    Type = BadgeLeaderboardType.ByRarity,
                    Rarity = 2,
                    ChunkIndex = 1,
                    ChunkSize = 50,
                    TotalEntries = 137,
                    Entries =
                    [
                        new BadgeLeaderboardEntrySnapshot
                        {
                            PlayerId = 7,
                            Name = "Ctuto",
                            Figure = "hd-180-1",
                            Rank = 51,
                            Score = 9,
                        },
                    ],
                    OwnEntry = new BadgeLeaderboardEntrySnapshot
                    {
                        PlayerId = 12,
                        Name = "Someone",
                        Figure = "hd-185-2",
                        Rank = 90,
                        Score = 4,
                    },
                },
            }
        );

        packet.PopInt().Should().Be(1, "BADGES_BY_RARITY is 1");
        packet.PopInt().Should().Be(2);
        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(50, "the client drops the message when this is not 50");
        packet.PopInt().Should().Be(137);

        packet.PopInt().Should().Be(1, "one row follows");
        packet.PopInt().Should().Be(7);
        packet.PopString().Should().Be("Ctuto");
        packet.PopString().Should().Be("hd-180-1");
        packet.PopInt().Should().Be(51);
        packet.PopInt().Should().Be(9);

        packet.PopBoolean().Should().BeTrue();
        packet.PopInt().Should().Be(12);
        packet.PopString().Should().Be("Someone");
        packet.PopString().Should().Be("hd-185-2");
        packet.PopInt().Should().Be(90);
        packet.PopInt().Should().Be(4);
    }

    [Fact]
    public void BadgeLeaderboardResult_WithoutOwnEntry_WritesTheBooleanAndStops()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(BadgeLeaderboardResultMessageComposer),
            new BadgeLeaderboardResultMessageComposer
            {
                Page = new BadgeLeaderboardPageSnapshot
                {
                    Type = BadgeLeaderboardType.TotalBadges,
                    Rarity = -1,
                    ChunkIndex = 0,
                    ChunkSize = 50,
                    TotalEntries = 0,
                    Entries = ImmutableArray<BadgeLeaderboardEntrySnapshot>.Empty,
                    OwnEntry = null,
                },
            }
        );

        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(-1, "DEFAULT_RARITY is -1 on the boards with no tier");
        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(50);
        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(0, "no rows");
        packet.PopBoolean().Should().BeFalse();
    }
}
