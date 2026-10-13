using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec M2a §3.1–3.2: the NPC, dialogue and quest messages, field by field in the client's order.</summary>
public sealed class RpgQuestWireTests
{
    private static readonly Rev Revision = new();

    private static (int Header, ClientPacket Body) Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return (BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), new ClientPacket(0, body));
    }

    private static ClientPacket Body(int header, params object[] fields)
    {
        List<byte> bytes = [];

        foreach (object field in fields)
        {
            if (field is int i)
            {
                byte[] b = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(b, i);
                bytes.AddRange(b);
            }
            else if (field is string s)
            {
                byte[] text = Encoding.UTF8.GetBytes(s);
                byte[] length = new byte[2];
                BinaryPrimitives.WriteInt16BigEndian(length, (short)text.Length);
                bytes.AddRange(length);
                bytes.AddRange(text);
            }
        }

        return new ClientPacket(header, bytes.ToArray());
    }

    [Fact]
    public void TheClientsThreeMessages_Parse()
    {
        ((RpgNpcTalkMessage)Revision.Parsers[9018].Parse(Body(9018, 42))).ObjectId.Should().Be(42);
        ((RpgDialogueChooseMessage)Revision.Parsers[9019].Parse(Body(9019, 1)))
            .Choice.Should()
            .Be(1);
        ((RpgQuestAbandonMessage)Revision.Parsers[9020].Parse(Body(9020, "quest.camp.wolves")))
            .QuestId.Should()
            .Be("quest.camp.wolves");
    }

    [Fact]
    public void Npcs_AreObjectIdThenMarker()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgNpcsMessageComposer
            {
                Npcs =
                [
                    new RpgNpcMarker { ObjectId = 5, Marker = "available" },
                    new RpgNpcMarker { ObjectId = 6, Marker = "" },
                ],
            }
        );

        header.Should().Be(9032);
        p.PopInt().Should().Be(2);
        (p.PopInt(), p.PopString()).Should().Be((5, "available"));
        (p.PopInt(), p.PopString()).Should().Be((6, ""));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void ADialogue_IsItsNpcSpeakerTextThenChoices()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgDialogueMessageComposer
            {
                NpcObjectId = 5,
                Speaker = "dialogue.camp.elder.name",
                Text = "dialogue.camp.elder.offer",
                Choices =
                [
                    new RpgDialogueChoice { Text = "dialogue.common.accept", Kind = "accept" },
                ],
            }
        );

        header.Should().Be(9033);
        (p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((5, "dialogue.camp.elder.name", "dialogue.camp.elder.offer"));
        p.PopInt().Should().Be(1);
        (p.PopString(), p.PopString()).Should().Be(("dialogue.common.accept", "accept"));
        p.Remaining.Should().Be(0);

        (int close, ClientPacket empty) = Serialize(new RpgDialogueCloseMessageComposer());
        close.Should().Be(9034);
        empty.Remaining.Should().Be(0);
    }

    [Fact]
    public void TheQuestLog_IsEachQuestStateThenObjectives()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgQuestLogMessageComposer
            {
                Quests =
                [
                    new RpgQuestLogEntry
                    {
                        QuestId = "quest.camp.wolves",
                        State = "active",
                        Objectives =
                        [
                            new RpgQuestObjectiveLine
                            {
                                Kind = "kill",
                                Current = 1,
                                Required = 3,
                            },
                        ],
                    },
                ],
            }
        );

        header.Should().Be(9035);
        p.PopInt().Should().Be(1);
        (p.PopString(), p.PopString()).Should().Be(("quest.camp.wolves", "active"));
        p.PopInt().Should().Be(1);
        (p.PopString(), p.PopInt(), p.PopInt()).Should().Be(("kill", 1, 3));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void AResult_IsActionCodeQuest()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgQuestResultMessageComposer
            {
                Action = "accepted",
                Code = "ok",
                QuestId = "quest.camp.wolves",
            }
        );

        header.Should().Be(9036);
        (p.PopString(), p.PopString(), p.PopString())
            .Should()
            .Be(("accepted", "ok", "quest.camp.wolves"));
        p.Remaining.Should().Be(0);
    }
}
