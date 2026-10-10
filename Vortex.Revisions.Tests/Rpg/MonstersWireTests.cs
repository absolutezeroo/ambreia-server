using System;
using System.Buffers.Binary;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec M1c §9: telegraphs and yells, field by field in the order the client reads them.</summary>
public sealed class MonstersWireTests
{
    private static readonly Rev Revision = new();

    private static (int Header, ClientPacket Body) Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return (BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), new ClientPacket(0, body));
    }

    [Fact]
    public void Telegraphs_AreReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgTelegraphsMessageComposer
            {
                Telegraphs =
                [
                    new RpgTelegraph
                    {
                        CasterObjectId = 40,
                        SkillId = "skill.monster.quake",
                        Shape = "circle",
                        DelayMs = 2000,
                        Cells = [(5, 5), (4, 5), (6, 5), (5, 4), (5, 6)],
                    },
                    new RpgTelegraph
                    {
                        CasterObjectId = 41,
                        SkillId = "skill.monster.root_lash",
                        Shape = "line",
                        DelayMs = 1500,
                        Cells = [(1, 0), (2, 0), (3, 0)],
                    },
                ],
            }
        );

        header.Should().Be(9025);
        p.PopInt().Should().Be(2);
        (p.PopInt(), p.PopString(), p.PopString(), p.PopInt())
            .Should()
            .Be((40, "skill.monster.quake", "circle", 2000));
        p.PopInt().Should().Be(5);
        (p.PopInt(), p.PopInt()).Should().Be((5, 5));
        (p.PopInt(), p.PopInt()).Should().Be((4, 5));
        (p.PopInt(), p.PopInt()).Should().Be((6, 5));
        (p.PopInt(), p.PopInt()).Should().Be((5, 4));
        (p.PopInt(), p.PopInt()).Should().Be((5, 6));
        (p.PopInt(), p.PopString(), p.PopString(), p.PopInt())
            .Should()
            .Be((41, "skill.monster.root_lash", "line", 1500));
        p.PopInt().Should().Be(3);
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt())
            .Should()
            .Be((1, 0, 2, 0, 3, 0));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void AYell_IsReadAsObjectAndTextKey()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgMonsterYellMessageComposer
            {
                ObjectId = 40,
                TextKey = "monster.grove.guardian.phase2",
            }
        );

        header.Should().Be(9026);
        (p.PopInt(), p.PopString()).Should().Be((40, "monster.grove.guardian.phase2"));
        p.Remaining.Should().Be(0);
    }
}
