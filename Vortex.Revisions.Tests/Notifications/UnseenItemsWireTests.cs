using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Notifications;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Notifications;

/// <summary>
/// The unseen-item loop, both directions. The outgoing shape is pinned to a 2026-09-18 capture of
/// the official server; the two incoming ones to the client's own UnseenItemTracker, whose
/// sendResetCategoryMessage writes one int and whose sendResetItemsMessage writes a category, a
/// count and that many ids. Both parsers used to read none of it.
/// </summary>
public sealed class UnseenItemsWireTests
{
    private const int ResetUnseenItemsEvent = 699;
    private const int ResetUnseenItemIdsEvent = 3771;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket BuildClientPacket(int header, Action<ServerPacket> write)
    {
        ServerPacket sp = new(header);
        write(sp);
        return new ClientPacket(header, sp.ToArray());
    }

    [Fact]
    public void UnseenItemsSerializer_MatchesTheOfficialCapture()
    {
        // Habbo sent 00000001 00000001 00000001 e6a71ac1 after a purchase: one category, id 1
        // (OWNED_FURNI), one item, and the same id its furniture list led with.
        UnseenItemsEventMessageComposer composer = new()
        {
            Categories = [new UnseenItemCategory { CategoryId = 1, ItemIds = [-425256255] }],
        };

        byte[] frame = Revision
            .Serializers[typeof(UnseenItemsEventMessageComposer)]
            .Serialize(composer)
            .ToArray();

        // Four bytes of length and two of header sit in front of the body.
        string body = Convert.ToHexString(frame.Skip(6).ToArray()).ToLowerInvariant();

        body.Should().Be("000000010000000100000001e6a71ac1");
    }

    [Fact]
    public void ResetUnseenItemsParser_ReadsTheCategory()
    {
        ClientPacket packet = BuildClientPacket(ResetUnseenItemsEvent, sp => sp.WriteInteger(4));

        ResetUnseenItemsMessage message = Revision
            .Parsers[ResetUnseenItemsEvent]
            .Parse(packet)
            .Should()
            .BeOfType<ResetUnseenItemsMessage>()
            .Subject;

        message.Category.Should().Be(4);
    }

    [Fact]
    public void ResetUnseenItemIdsParser_ReadsTheCategoryAndEveryId()
    {
        ClientPacket packet = BuildClientPacket(
            ResetUnseenItemIdsEvent,
            sp =>
                sp.WriteInteger(1)
                    .WriteInteger(3)
                    .WriteInteger(10)
                    .WriteInteger(20)
                    .WriteInteger(30)
        );

        ResetUnseenItemIdsMessage message = Revision
            .Parsers[ResetUnseenItemIdsEvent]
            .Parse(packet)
            .Should()
            .BeOfType<ResetUnseenItemIdsMessage>()
            .Subject;

        message.Category.Should().Be(1);
        message.ItemIds.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void ResetUnseenItemIdsParser_IgnoresACountThePayloadDoesNotBackUp()
    {
        // A peer claiming two billion ids must not size anything before the first read fails.
        ClientPacket packet = BuildClientPacket(
            ResetUnseenItemIdsEvent,
            sp => sp.WriteInteger(1).WriteInteger(int.MaxValue).WriteInteger(7)
        );

        ResetUnseenItemIdsMessage message = Revision
            .Parsers[ResetUnseenItemIdsEvent]
            .Parse(packet)
            .Should()
            .BeOfType<ResetUnseenItemIdsMessage>()
            .Subject;

        message.ItemIds.Should().Equal(7);
    }

    [Fact]
    public void ResetUnseenItemIdsParser_AcceptsAnEmptyList()
    {
        ClientPacket packet = BuildClientPacket(
            ResetUnseenItemIdsEvent,
            sp => sp.WriteInteger(2).WriteInteger(0)
        );

        ResetUnseenItemIdsMessage message = Revision
            .Parsers[ResetUnseenItemIdsEvent]
            .Parse(packet)
            .Should()
            .BeOfType<ResetUnseenItemIdsMessage>()
            .Subject;

        message.Category.Should().Be(2);
        message.ItemIds.Should().BeEmpty();
    }
}
