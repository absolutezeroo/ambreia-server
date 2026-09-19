using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.FriendList;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
/// Header 1773, the heart/smile/skull picker on a console entry.
/// </summary>
/// <remarks>
/// The message, its parser, its handler and the grain method behind it all existed and none of it
/// could ever run: no map bound 1773, so the packet arrived as an unknown header and was dropped.
/// A handler that compiles is not a handler that is reachable, which is what this asserts.
/// </remarks>
public sealed class SetRelationshipStatusWireTests
{
    private const int SetRelationshipStatusMessageEvent = 1773;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Theory]
    [InlineData(0, MessengerFriendRelationType.Zero)]
    [InlineData(1, MessengerFriendRelationType.One)]
    [InlineData(2, MessengerFriendRelationType.Two)]
    [InlineData(3, MessengerFriendRelationType.Three)]
    public void Request_ReadsTheFriendIdThenTheRelation(
        int wireValue,
        MessengerFriendRelationType expected
    )
    {
        // Two int32s, in that order: user_id then relation_id, per the target client's composer.
        byte[] body = [0, 0, 0, 42, 0, 0, 0, (byte)wireValue];

        Revision
            .Parsers[SetRelationshipStatusMessageEvent]
            .Parse(new ClientPacket(SetRelationshipStatusMessageEvent, body))
            .Should()
            .BeOfType<SetRelationshipStatusMessage>()
            .Which.Should()
            .BeEquivalentTo(new { FriendId = 42, RelationType = expected });
    }
}
