using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;
using Vortex.Protocol.Messages.Outgoing.Rpg.Items;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec Objets §17: the bag and equipment packets, field by field in the order the client
/// reads them.</summary>
public sealed class RpgItemsWireTests
{
    private static readonly Rev Revision = new();

    private static ClientPacket Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }

    /// <summary>A client body of int32 BE fields.</summary>
    private static ClientPacket Body(int header, params int[] fields)
    {
        List<byte> bytes = [];

        foreach (int field in fields)
        {
            byte[] number = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, field);
            bytes.AddRange(number);
        }

        return new ClientPacket(header, bytes.ToArray());
    }

    [Fact]
    public void AMove_ReadsFromThenTo()
    {
        RpgItemMoveMessage m = (RpgItemMoveMessage)Revision.Parsers[9004].Parse(Body(9004, 2, 7));

        (m.From, m.To).Should().Be((2, 7));
    }

    [Fact]
    public void ASplit_ReadsFromQuantityTo()
    {
        RpgItemSplitMessage m = (RpgItemSplitMessage)
            Revision.Parsers[9005].Parse(Body(9005, 2, 5, 7));

        (m.From, m.Quantity, m.To).Should().Be((2, 5, 7));
    }

    [Fact]
    public void AnEquip_ReadsTheCase() =>
        ((RpgItemEquipMessage)Revision.Parsers[9006].Parse(Body(9006, 4))).BagSlot.Should().Be(4);

    [Fact]
    public void AnUnequip_ReadsTheSlot() =>
        ((RpgItemUnequipMessage)Revision.Parsers[9007].Parse(Body(9007, 8)))
            .EquipmentSlot.Should()
            .Be(8);

    [Fact]
    public void ADestroy_ReadsCaseAndQuantity()
    {
        RpgItemDestroyMessage m = (RpgItemDestroyMessage)
            Revision.Parsers[9008].Parse(Body(9008, 3, 2));

        (m.BagSlot, m.Quantity).Should().Be((3, 2));
    }

    [Fact]
    public void AClaim_ReadsTheParcel() =>
        ((RpgItemClaimParcelMessage)Revision.Parsers[9009].Parse(Body(9009, 1)))
            .Parcel.Should()
            .Be(1);

    [Fact]
    public void TheInventory_IsReadInTheClientsOrder()
    {
        ClientPacket p = Serialize(
            new RpgInventoryMessageComposer
            {
                BagSize = 30,
                MaxParcels = 50,
                Items =
                [
                    new CharacterItemSnapshot
                    {
                        ItemId = 11,
                        DefinitionId = "item.material.amber_shard",
                        Quantity = 20,
                        Durability = null,
                        Bound = false,
                        Container = ItemContainer.Bag,
                        Slot = 0,
                    },
                    new CharacterItemSnapshot
                    {
                        ItemId = 12,
                        DefinitionId = "item.weapon.rusted_sword",
                        Quantity = 1,
                        Durability = 50,
                        Bound = true,
                        Container = ItemContainer.Equipment,
                        Slot = 5,
                    },
                ],
            }
        );

        (p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((30, 50, 2));
        (p.PopInt(), p.PopString(), p.PopInt(), p.PopInt())
            .Should()
            .Be((11, "item.material.amber_shard", 20, -1));
        (p.PopBoolean(), p.PopInt(), p.PopInt()).Should().Be((false, 0, 0));
        (p.PopInt(), p.PopString(), p.PopInt(), p.PopInt())
            .Should()
            .Be((12, "item.weapon.rusted_sword", 1, 50));
        (p.PopBoolean(), p.PopInt(), p.PopInt()).Should().Be((true, 1, 5));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void AResult_IsActionThenCode()
    {
        ClientPacket p = Serialize(
            new RpgItemResultMessageComposer
            {
                Action = RpgItemResultMessageComposer.ActionEquip,
                Code = "wrong_class",
            }
        );

        (p.PopInt(), p.PopString()).Should().Be((3, "wrong_class"));
        p.Remaining.Should().Be(0);
    }
}
