using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Chat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Chat;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Rpg;

/// <summary>Spec F3c §10: the chat packets, field by field in the order the client reads them.</summary>
public sealed class RpgChatWireTests
{
    private static readonly Rev Revision = new();

    private static ClientPacket Serialize(IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        return new ClientPacket(0, body);
    }

    /// <summary>A client body: strings as int16 length + UTF-8.</summary>
    private static ClientPacket Body(int header, params string[] fields)
    {
        List<byte> bytes = [];

        foreach (string field in fields)
        {
            byte[] text = Encoding.UTF8.GetBytes(field);
            byte[] length = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(length, (short)text.Length);
            bytes.AddRange(length);
            bytes.AddRange(text);
        }

        return new ClientPacket(header, bytes.ToArray());
    }

    [Fact]
    public void AChatSend_ReadsChannelTargetText()
    {
        RpgChatSendMessage m = (RpgChatSendMessage)
            Revision.Parsers[9011].Parse(Body(9011, "whisper", "Bob", "salut"));

        (m.Channel, m.Target, m.Text).Should().Be(("whisper", "Bob", "salut"));
    }

    [Fact]
    public void AChatLine_IsReadInTheClientsOrder()
    {
        ClientPacket p = Serialize(
            new RpgChatMessageComposer
            {
                Channel = "whisper",
                SenderId = 7,
                SenderName = "Ann",
                Text = "salut [item:x]",
                SentAt = 1760000000,
                Target = "Bob",
            }
        );

        (p.PopString(), p.PopInt(), p.PopString(), p.PopString(), p.PopInt(), p.PopString())
            .Should()
            .Be(("whisper", 7, "Ann", "salut [item:x]", 1760000000, "Bob"));
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void AChatResult_IsReadInTheClientsOrder()
    {
        ClientPacket p = Serialize(
            new RpgChatResultMessageComposer { Channel = "guild", Code = "not_in_guild" }
        );

        (p.PopString(), p.PopString()).Should().Be(("guild", "not_in_guild"));
        p.Remaining.Should().Be(0);
    }
}
