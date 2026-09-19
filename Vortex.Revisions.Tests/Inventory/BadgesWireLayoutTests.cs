using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Inventory;

/// <summary>
///     Locks the badge-list byte contract at four fields per badge.
///
///     This serializer wrote two — slot and code — where WIN63's parser
///     (unknowns/_SafePkg_3206/_SafeCls_3564.as) reads four and builds a Badge from all of them.
///     With more than one badge in the list the client's next slot id came out of the middle of a
///     string, so the failure was a desynced list rather than a missing rarity tier. Its sibling
///     message, <c>BadgeReceivedEvent</c>, already wrote all four.
/// </summary>
public sealed class BadgesWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket SerializeAndReadBody(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    [Fact]
    public void BadgesEvent_WritesFourFieldsPerBadge()
    {
        BadgesEventMessageComposer composer = new()
        {
            Badges =
            [
                new PlayerBadgeSnapshot
                {
                    SlotId = 1,
                    BadgeCode = "ADM",
                    OwnerCount = 12,
                    BadgeRarityId = 3,
                },
                new PlayerBadgeSnapshot
                {
                    SlotId = 0,
                    BadgeCode = "ACH_Login5",
                    OwnerCount = 900,
                    BadgeRarityId = 0,
                },
            ],
        };

        ClientPacket packet = SerializeAndReadBody(typeof(BadgesEventMessageComposer), composer);

        packet.PopInt().Should().Be(1); // totalFragments
        packet.PopInt().Should().Be(0); // fragmentNo, zero-based as in a live official capture
        packet.PopInt().Should().Be(2); // badge count

        packet.PopInt().Should().Be(1);
        packet.PopString().Should().Be("ADM");
        packet.PopInt().Should().Be(12);
        packet.PopInt().Should().Be(3);

        // The second badge only lands here if the first wrote all four. With two fields per badge
        // this read used to come back out of the middle of the previous string.
        packet.PopInt().Should().Be(0);
        packet.PopString().Should().Be("ACH_Login5");
        packet.PopInt().Should().Be(900);
        packet.PopInt().Should().Be(0);
    }

    [Fact]
    public void BadgesEvent_EmptyListStillWritesItsHeader()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(BadgesEventMessageComposer),
            new BadgesEventMessageComposer { Badges = ImmutableArray<PlayerBadgeSnapshot>.Empty }
        );

        packet.PopInt().Should().Be(1); // totalFragments
        packet.PopInt().Should().Be(0); // fragmentNo
        packet.PopInt().Should().Be(0); // badge count
    }
}
