using System;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
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
}
