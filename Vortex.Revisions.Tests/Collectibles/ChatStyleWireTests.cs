using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Nft;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Collectibles;

/// <summary>
/// The three chat-style answers, pinned against the client's own parsers.
/// </summary>
/// <remarks>
/// Both list messages are a count and that many ints, and the incremental one is a boolean before
/// its id. None of that is guessable from the field names: the two lists are read by different
/// classes that happen to be identical, and the incremental message's getters are declared in the
/// opposite order to nothing — here, unusually for this domain, constructor order and getter order
/// agree. The value of pinning it is that the next reader does not have to re-derive that.
/// </remarks>
public sealed class ChatStyleWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket Body(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }

    [Fact]
    public void UnlockedStyles_AreACountThenThatManyIds()
    {
        ClientPacket packet = Body(
            typeof(UserNftChatStylesMessageComposer),
            new UserNftChatStylesMessageComposer { ChatStyleIds = [1004, 1017] }
        );

        packet.PopInt().Should().Be(2);
        packet.PopInt().Should().Be(1004);
        packet.PopInt().Should().Be(1017);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void NoUnlockedStyles_IsAZeroCountAndNotAnEmptyBody()
    {
        // The client reads the count unconditionally. An empty body would leave it reading past the
        // end rather than concluding the player owns nothing.
        ClientPacket packet = Body(
            typeof(UserNftChatStylesMessageComposer),
            new UserNftChatStylesMessageComposer { ChatStyleIds = [] }
        );

        packet.PopInt().Should().Be(0);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void PurchasableStyles_AreTheSameShapeAsTheUnlockedOnes()
    {
        ClientPacket packet = Body(
            typeof(UserPurchasableChatStylesMessageComposer),
            new UserPurchasableChatStylesMessageComposer { ChatStyleIds = [10000] }
        );

        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(10000);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void PurchasableChange_WritesTheFlagBeforeTheId()
    {
        ClientPacket packet = Body(
            typeof(UserPurchasableChatStyleChangedMessageComposer),
            new UserPurchasableChatStyleChangedMessageComposer { Added = true, ChatStyleId = 10000 }
        );

        packet.PopBoolean().Should().BeTrue();
        packet.PopInt().Should().Be(10000);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void PurchasableChange_CarriesTheIdEvenWhenItIsBeingTakenBack()
    {
        // The removal names the style: the client looks it up in its vector and splices it out, so
        // a body that dropped the id on a false flag would remove nothing.
        ClientPacket packet = Body(
            typeof(UserPurchasableChatStyleChangedMessageComposer),
            new UserPurchasableChatStyleChangedMessageComposer
            {
                Added = false,
                ChatStyleId = 10000,
            }
        );

        packet.PopBoolean().Should().BeFalse();
        packet.PopInt().Should().Be(10000);
        packet.Remaining.Should().Be(0);
    }
}
