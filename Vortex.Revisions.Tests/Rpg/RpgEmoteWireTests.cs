using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Emote;
using Vortex.Protocol.Messages.Outgoing.Rpg.Emote;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>RP actions on the wire: in 9012 (key), out 9017 (object id, key).</summary>
public sealed class RpgEmoteWireTests
{
    private static readonly Rev Revision = new();

    [Fact]
    public void AnEmote_ReadsItsKey()
    {
        byte[] text = Encoding.UTF8.GetBytes("wave");
        byte[] body = new byte[2 + text.Length];
        BinaryPrimitives.WriteInt16BigEndian(body, (short)text.Length);
        text.CopyTo(body, 2);

        ((RpgEmoteMessage)Revision.Parsers[9012].Parse(new ClientPacket(9012, body)))
            .Key.Should()
            .Be("wave");
    }

    [Fact]
    public void AnEmoteShown_IsReadInTheClientsOrder()
    {
        byte[] bytes = Revision
            .Serializers[typeof(RpgEmoteMessageComposer)]
            .Serialize(new RpgEmoteMessageComposer { ObjectId = 7, Key = "dance" })
            .ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        ClientPacket p = new(0, body);

        BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)).Should().Be(9017);
        (p.PopInt(), p.PopString()).Should().Be((7, "dance"));
        p.Remaining.Should().Be(0);
    }
}
