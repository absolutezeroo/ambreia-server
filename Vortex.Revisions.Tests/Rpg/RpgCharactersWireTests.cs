using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>
/// Spec F3a §6: the character screen and character state packets, field by field in the order the
/// client reads them — the client session implements the same contract from that table.
/// </summary>
public sealed class RpgCharactersWireTests
{
    private static readonly Rev Revision = new();

    private static readonly CharacterStatsSnapshot Stats = new()
    {
        Strength = 12,
        Intelligence = 4,
        Agility = 6,
        MaxHealth = 120,
        MaxMana = 60,
        Armor = 4,
        CritPercent = 6.2,
        DodgePercent = 0.6,
        SpeedPercent = 100,
        Power = 12,
    };

    private static ClientPacket Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }

    /// <summary>A client body: ints as int32 BE, strings as int16 length + UTF-8.</summary>
    private static ClientPacket Body(int header, params object[] fields)
    {
        List<byte> bytes = [];

        foreach (object field in fields)
        {
            if (field is string s)
            {
                byte[] text = Encoding.UTF8.GetBytes(s);
                byte[] length = new byte[2];
                BinaryPrimitives.WriteInt16BigEndian(length, (short)text.Length);
                bytes.AddRange(length);
                bytes.AddRange(text);
            }
            else
            {
                byte[] number = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(number, (int)field);
                bytes.AddRange(number);
            }
        }

        return new ClientPacket(header, bytes.ToArray());
    }

    private static void ExpectStatBlock(ClientPacket p)
    {
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt())
            .Should()
            .Be((12, 4, 6, 120, 60, 4));
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((620, 60, 100, 12));
    }

    [Fact]
    public void TheCharacterList_IsReadInTheClientsOrder()
    {
        ClientPacket p = Serialize(
            new RpgCharacterListMessageComposer
            {
                MaxCharacters = 5,
                Characters =
                [
                    new RpgCharacterListEntry
                    {
                        PlayerId = 7,
                        Name = "Bob",
                        Figure = "hd-180-1",
                        Gender = "M",
                        ClassId = "class.classless",
                        SpecializationId = "spec.classless.adventurer",
                        Level = 1,
                        Starter = true,
                    },
                    new RpgCharacterListEntry
                    {
                        PlayerId = 8,
                        Name = "Ann",
                        Figure = "hd-600-1",
                        Gender = "F",
                        ClassId = "class.warrior",
                        SpecializationId = "spec.warrior.guardian",
                        Level = 3,
                        Starter = false,
                    },
                ],
            }
        );

        (p.PopInt(), p.PopInt()).Should().Be((5, 2));
        (p.PopInt(), p.PopString(), p.PopString(), p.PopString())
            .Should()
            .Be((7, "Bob", "hd-180-1", "M"));
        (p.PopString(), p.PopString(), p.PopInt(), p.PopBoolean())
            .Should()
            .Be(("class.classless", "spec.classless.adventurer", 1, true));
        (p.PopInt(), p.PopString(), p.PopString(), p.PopString())
            .Should()
            .Be((8, "Ann", "hd-600-1", "F"));
        (p.PopString(), p.PopString(), p.PopInt(), p.PopBoolean())
            .Should()
            .Be(("class.warrior", "spec.warrior.guardian", 3, false));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void TheClassList_CarriesItsSpecializations()
    {
        ClientPacket p = Serialize(
            new RpgClassListMessageComposer
            {
                Classes =
                [
                    new RpgClassEntry
                    {
                        Id = "class.warrior",
                        Role = "tank",
                        Range = 1,
                        PrimaryStat = "strength",
                        Starter = false,
                        Specializations = ["spec.warrior.guardian"],
                    },
                ],
            }
        );

        p.PopInt().Should().Be(1);
        (p.PopString(), p.PopString(), p.PopInt(), p.PopString(), p.PopBoolean())
            .Should()
            .Be(("class.warrior", "tank", 1, "strength", false));
        (p.PopInt(), p.PopString()).Should().Be((1, "spec.warrior.guardian"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void AResult_CarriesActionCodeAndPlayer()
    {
        ClientPacket p = Serialize(
            new RpgCharacterResultMessageComposer
            {
                Action = RpgCharacterResultMessageComposer.ActionCreate,
                Code = "character.name_taken",
                PlayerId = 0,
            }
        );

        (p.PopInt(), p.PopString(), p.PopInt()).Should().Be((1, "character.name_taken", 0));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void TheState_EndsWithTheStatBlock()
    {
        ClientPacket p = Serialize(
            new RpgCharacterStateMessageComposer
            {
                PlayerId = 7,
                ClassId = "class.warrior",
                SpecializationId = "spec.warrior.guardian",
                Level = 1,
                MaxLevel = 20,
                Xp = 0,
                XpToNext = 100,
                Stats = Stats,
            }
        );

        (p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((7, "class.warrior", "spec.warrior.guardian"));
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((1, 20, 0, 100));
        ExpectStatBlock(p);
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void Progress_AndStats_AreFullyConsumed()
    {
        ClientPacket progress = Serialize(
            new RpgCharacterProgressMessageComposer
            {
                Level = 3,
                Xp = 0,
                XpToNext = 220,
            }
        );
        ClientPacket stats = Serialize(new RpgCharacterStatsMessageComposer { Stats = Stats });

        (progress.PopInt(), progress.PopInt(), progress.PopInt()).Should().Be((3, 0, 220));
        progress.Remaining.Should().Be(0);
        ExpectStatBlock(stats);
        stats.Remaining.Should().Be(0);
    }

    [Fact]
    public void ACreate_ReadsNameFigureGender()
    {
        RpgCharacterCreateMessage m = (RpgCharacterCreateMessage)
            Revision.Parsers[9001].Parse(Body(9001, "Bob", "hd-180-1", "M"));

        (m.Name, m.Figure, m.Gender).Should().Be(("Bob", "hd-180-1", "M"));
    }

    [Fact]
    public void AClassChoice_ReadsPlayerAndClass()
    {
        RpgCharacterChooseClassMessage m = (RpgCharacterChooseClassMessage)
            Revision.Parsers[9002].Parse(Body(9002, 7, "class.warrior"));

        (m.PlayerId, m.ClassId).Should().Be((7, "class.warrior"));
    }

    [Fact]
    public void EnterWorld_ReadsThePlayer() =>
        ((RpgEnterWorldMessage)Revision.Parsers[9003].Parse(Body(9003, 7))).PlayerId.Should().Be(7);

    [Fact]
    public void TheListRequest_HasNoField() =>
        Revision
            .Parsers[9000]
            .Parse(Body(9000))
            .Should()
            .BeOfType<RpgCharacterListRequestMessage>();

    [Fact]
    public void EveryCharacterScreenMessage_IsAnAccountPhaseMessage() =>
        new object[]
        {
            Revision.Parsers[9000].Parse(Body(9000)),
            Revision.Parsers[9001].Parse(Body(9001, "a", "b", "M")),
            Revision.Parsers[9002].Parse(Body(9002, 1, "c")),
            Revision.Parsers[9003].Parse(Body(9003, 1)),
        }
            .Should()
            .AllBeAssignableTo<IAccountPhaseMessage>();
}
