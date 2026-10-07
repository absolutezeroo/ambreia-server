using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Marketplace;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Incoming.Quest;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
///     Five parsers that read nothing at all while their client composer pushes one or two values.
///     Unread trailing bytes do not desynchronise a length-framed packet, so none of these was a
///     parsing crash — they were fields the server acted without. Two of them had already been
///     declared on the message record and were therefore structurally always default, which is the
///     worst of the shapes: <c>RemoveOwnRoomRightsRoom</c>'s handler guards on
///     <c>message.RoomId &gt; 0</c>, so leaving it 0 made the whole feature return silently.
///
///     Every id is cross-checked against the client's own registry
///     (<c>communication/_SafeCls_2046.as</c>), not against the header table's comments.
/// </summary>
public sealed class UnreadFieldWireTests
{
    private const int GetMarketplaceOwnOffersEvent = 2086; // _composers[2086] = _SafeCls_1881
    private const int RemoveOwnRoomRightsRoomEvent = 260; // _composers[260]  = _SafeCls_2158
    private const int GetDailyQuestEvent = 397; // _composers[397]  = _SafeCls_3520

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static T Parse<T>(int header, Action<ServerPacket> write)
        where T : class
    {
        ServerPacket sp = new(header);
        write(sp);
        return Revision
            .Parsers[header]
            .Parse(new ClientPacket(header, sp.ToArray()))
            .Should()
            .BeOfType<T>()
            .Subject;
    }

    /// <summary>
    ///     The one that was a dead feature rather than a dropped field: the handler already read
    ///     <c>message.RoomId</c> and returned when it was not positive, and nothing ever set it.
    /// </summary>
    [Fact]
    public void RemoveOwnRoomRightsRoomParser_ReadsTheRoomTheClientNames()
    {
        RemoveOwnRoomRightsRoomMessage message = Parse<RemoveOwnRoomRightsRoomMessage>(
            RemoveOwnRoomRightsRoomEvent,
            sp => sp.WriteInteger(4271)
        );

        message.RoomId.Should().Be(4271);
    }

    [Fact]
    public void GetMarketplaceOwnOffersParser_ReadsItsLeadingInt()
    {
        GetMarketplaceOwnOffersMessage message = Parse<GetMarketplaceOwnOffersMessage>(
            GetMarketplaceOwnOffersEvent,
            sp => sp.WriteInteger(1)
        );

        message.Unknown1.Should().Be(1);
    }

    [Fact]
    public void GetDailyQuestParser_ReadsTheRefreshFlagBeforeTheIndex()
    {
        GetDailyQuestMessage message = Parse<GetDailyQuestMessage>(
            GetDailyQuestEvent,
            sp => sp.WriteBoolean(true).WriteInteger(0)
        );

        message.Refresh.Should().BeTrue();
        message.Index.Should().Be(0);
    }
}
