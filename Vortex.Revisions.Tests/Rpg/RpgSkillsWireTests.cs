using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec M1b §10: the skill messages, field by field in the order the client reads them.</summary>
public sealed class RpgSkillsWireTests
{
    private static readonly Rev Revision = new();

    private static (int Header, ClientPacket Body) Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return (BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), new ClientPacket(0, body));
    }

    /// <summary>A client body: ints as int32, strings as int16 length + UTF-8, in order.</summary>
    private static IMessageEvent Parse(int header, params object[] fields)
    {
        List<byte> bytes = [];

        foreach (object field in fields)
        {
            if (field is int value)
            {
                byte[] number = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(number, value);
                bytes.AddRange(number);

                continue;
            }

            byte[] text = Encoding.UTF8.GetBytes((string)field);
            byte[] length = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(length, (short)text.Length);
            bytes.AddRange(length);
            bytes.AddRange(text);
        }

        return Revision.Parsers[header].Parse(new ClientPacket(header, bytes.ToArray()));
    }

    [Fact]
    public void SkillCast_IsReadAsSkillTargetAndCell()
    {
        RpgSkillCastMessage cast = (RpgSkillCastMessage)Parse(
            9013,
            "skill.mage.firebolt",
            12,
            -1,
            -1
        );

        (cast.SkillId, cast.TargetObjectId, cast.X, cast.Y)
            .Should()
            .Be(("skill.mage.firebolt", 12, -1, -1));
    }

    [Fact]
    public void ActionBarSet_IsReadAsSlotAndAction()
    {
        RpgActionBarSetMessage set = (RpgActionBarSetMessage)Parse(
            9014,
            2,
            "skill:skill.mage.firebolt"
        );

        (set.Slot, set.ActionId).Should().Be((2, "skill:skill.mage.firebolt"));
    }

    [Fact]
    public void CastCancel_HasNoField() => Parse(9015).Should().BeOfType<RpgCastCancelMessage>();

    [Fact]
    public void Skills_WriteTheKnownThenTheBar()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgSkillsMessageComposer
            {
                Known = ["skill.mage.firebolt", "skill.mage.flame_burst"],
                Bar = ["skill:skill.mage.firebolt", ""],
            }
        );

        header.Should().Be(9018);
        (p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((2, "skill.mage.firebolt", "skill.mage.flame_burst"));
        (p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((2, "skill:skill.mage.firebolt", ""));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void SkillResult_WritesSkillThenCode()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgSkillResultMessageComposer
            {
                SkillId = "skill.mage.firebolt",
                Code = RpgSkillResultMessageComposer.GlobalCooldown,
            }
        );

        header.Should().Be(9019);
        (p.PopString(), p.PopString()).Should().Be(("skill.mage.firebolt", "global_cooldown"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void CastStart_WritesEachCast()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgCastStartMessageComposer
            {
                Casts =
                [
                    new RpgCastStart
                    {
                        CasterObjectId = 1,
                        SkillId = "skill.archer.volley",
                        DurationMs = 3000,
                        Channel = true,
                        TargetObjectId = 0,
                        X = 6,
                        Y = 4,
                    },
                ],
            }
        );

        header.Should().Be(9020);
        p.PopInt().Should().Be(1);
        (p.PopInt(), p.PopString(), p.PopInt(), p.PopBoolean(), p.PopInt(), p.PopInt(), p.PopInt())
            .Should()
            .Be((1, "skill.archer.volley", 3000, true, 0, 6, 4));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void CastEnd_WritesEachEnd()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgCastEndMessageComposer
            {
                Ends =
                [
                    new RpgCastEnd
                    {
                        CasterObjectId = 1,
                        SkillId = "skill.mage.firebolt",
                        Reason = RpgCastEnd.Interrupted,
                    },
                ],
            }
        );

        header.Should().Be(9021);
        (p.PopInt(), p.PopInt(), p.PopString(), p.PopString())
            .Should()
            .Be((1, 1, "skill.mage.firebolt", "interrupted"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void Cooldowns_WriteTheGlobalThenEachSkill()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgCooldownsMessageComposer
            {
                GlobalRemainingMs = 1000,
                Skills =
                [
                    new RpgCooldown { SkillId = "skill.mage.flame_burst", RemainingMs = 10000 },
                ],
            }
        );

        header.Should().Be(9022);
        (p.PopInt(), p.PopInt(), p.PopString(), p.PopInt())
            .Should()
            .Be((1000, 1, "skill.mage.flame_burst", 10000));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void Statuses_WriteEachFighterAndItsStatuses()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgStatusesMessageComposer
            {
                Combatants =
                [
                    new RpgCombatantStatuses
                    {
                        ObjectId = 9,
                        Statuses =
                        [
                            new RpgStatusEntry
                            {
                                StatusId = "status.burning",
                                RemainingMs = 4000,
                                Stacks = 2,
                            },
                        ],
                    },
                    new RpgCombatantStatuses { ObjectId = 3, Statuses = [] },
                ],
            }
        );

        header.Should().Be(9023);
        p.PopInt().Should().Be(2);
        (p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt())
            .Should()
            .Be((9, 1, "status.burning", 4000, 2));
        (p.PopInt(), p.PopInt()).Should().Be((3, 0));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void SkillEvents_WriteImpactsThenOutcomes()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgSkillEventsMessageComposer
            {
                Impacts =
                [
                    new RpgSkillImpact
                    {
                        CasterObjectId = 1,
                        SkillId = "skill.archer.volley",
                        TargetObjectId = 0,
                        X = 6,
                        Y = 4,
                    },
                ],
                Outcomes =
                [
                    new RpgSkillOutcome
                    {
                        SourceObjectId = 1,
                        SourceId = "skill.archer.volley",
                        TargetObjectId = 2,
                        Amount = 7,
                        Absorbed = 3,
                        Outcome = RpgSkillOutcome.Hit,
                        Health = 33,
                        MaxHealth = 40,
                        Killed = false,
                    },
                ],
            }
        );

        header.Should().Be(9024);
        (p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopInt())
            .Should()
            .Be((1, 1, "skill.archer.volley", 0, 6, 4));
        p.PopInt().Should().Be(1);
        (p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopString())
            .Should()
            .Be((1, "skill.archer.volley", 2, 7, 3, "hit"));
        (p.PopInt(), p.PopInt(), p.PopBoolean()).Should().Be((33, 40, false));
        p.Remaining.Should().Be(0);
    }
}
