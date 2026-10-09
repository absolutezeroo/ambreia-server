using System;
using System.Buffers.Binary;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec M1a §9.2: the combat messages, field by field in the order the client reads them.</summary>
public sealed class RpgCombatWireTests
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
    public void CombatEvents_AreReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgCombatEventsMessageComposer
            {
                Hits =
                [
                    new RpgCombatHit
                    {
                        AttackerId = 1,
                        TargetId = 9,
                        Amount = 12,
                        Outcome = RpgCombatHit.Crit,
                        Health = 28,
                        MaxHealth = 40,
                        Killed = false,
                    },
                    new RpgCombatHit
                    {
                        AttackerId = 9,
                        TargetId = 1,
                        Amount = 0,
                        Outcome = RpgCombatHit.Miss,
                        Health = 60,
                        MaxHealth = 60,
                        Killed = false,
                    },
                ],
            }
        );

        header.Should().Be(9012);
        p.PopInt().Should().Be(2);
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopBoolean())
            .Should()
            .Be((1, 9, 12, "crit", 28, 40, false));
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopBoolean())
            .Should()
            .Be((9, 1, 0, "miss", 60, 60, false));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void Vitals_AreReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgVitalsMessageComposer
            {
                Combatants =
                [
                    new RpgCombatantVitals
                    {
                        ObjectId = 9,
                        Health = 40,
                        MaxHealth = 40,
                    },
                    new RpgCombatantVitals
                    {
                        ObjectId = 1,
                        Health = 31,
                        MaxHealth = 60,
                    },
                ],
            }
        );

        header.Should().Be(9013);
        p.PopInt().Should().Be(2);
        (p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((9, 40, 40));
        (p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((1, 31, 60));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void SelfVitals_AreReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgSelfVitalsMessageComposer
            {
                Health = 31,
                MaxHealth = 60,
                Mana = 12,
                MaxMana = 40,
                InCombat = true,
            }
        );

        header.Should().Be(9014);
        (p.PopInt(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopBoolean())
            .Should()
            .Be((31, 60, 12, 40, true));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void ADeath_IsReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgDeathMessageComposer { RespawnSeconds = 5 }
        );

        header.Should().Be(9015);
        p.PopInt().Should().Be(5);
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void ACombatResult_IsReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgCombatResultMessageComposer { Code = RpgCombatResultMessageComposer.SafeZone }
        );

        header.Should().Be(9016);
        p.PopString().Should().Be("safe_zone");
        p.Remaining.Should().Be(0);
    }
}
