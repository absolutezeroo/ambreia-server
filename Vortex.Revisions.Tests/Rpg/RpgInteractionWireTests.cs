using System;
using System.Buffers.Binary;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Interaction;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec F3b §7: the interaction packets, field by field in the order the client reads them.</summary>
public sealed class RpgInteractionWireTests
{
    private static readonly Rev Revision = new();

    private static ClientPacket Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }

    [Fact]
    public void TheClickables_AreReadInTheClientsOrder()
    {
        ClientPacket p = Serialize(
            new RpgZoneInteractivesMessageComposer
            {
                Objects =
                [
                    new RpgZoneInteractive { ObjectId = 1, Interaction = "door" },
                    new RpgZoneInteractive { ObjectId = 4, Interaction = "sign" },
                ],
            }
        );

        p.PopInt().Should().Be(2);
        (p.PopInt(), p.PopString()).Should().Be((1, "door"));
        (p.PopInt(), p.PopString()).Should().Be((4, "sign"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void TheTarget_IsReadInTheClientsOrder()
    {
        ClientPacket p = Serialize(
            new RpgTargetMessageComposer
            {
                ObjectId = 202,
                Kind = RpgTargetMessageComposer.KindPlayer,
                Name = "Ann",
                Level = 3,
                ClassId = "class.warrior",
            }
        );

        (p.PopInt(), p.PopString(), p.PopString(), p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((202, "player", "Ann", 3, "class.warrior", "normal"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void AnEliteTarget_EndsWithItsRank()
    {
        ClientPacket p = Serialize(
            new RpgTargetMessageComposer
            {
                ObjectId = 31,
                Kind = RpgTargetMessageComposer.KindMonster,
                Name = "monster.forest.alpha_wolf",
                Level = 4,
                ClassId = string.Empty,
                Rank = RpgTargetMessageComposer.RankElite,
            }
        );

        (p.PopInt(), p.PopString(), p.PopString(), p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((31, "monster", "monster.forest.alpha_wolf", 4, "", "elite"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void ATargetSelect_ReadsTheObjectId()
    {
        byte[] body = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(body, 202);

        ((RpgTargetSelectMessage)Revision.Parsers[9010].Parse(new ClientPacket(9010, body)))
            .ObjectId.Should()
            .Be(202);
    }
}
