using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Advertisement;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
///     Two more serializers that were registered with an empty <c>Serialize</c> body while the
///     client's parser reads off them. Neither is built by a handler yet, so nothing was breaking
///     today — but the shape has to be right before the feature lands, not after someone spends an
///     afternoon on a mystery desync.
///
///     <c>node scripts/unlistened-server-messages.mjs</c> in the client repo lists the rest.
/// </summary>
public sealed class EmptySerializerWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket SerializeAndReadBody(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    /// <summary>
    ///     WIN63 unknowns/.../_SafeCls_3699.as: two ints and two strings. The first int is a flag —
    ///     the client maps 1 to wall-item and everything else to floor-item — so it is not a
    ///     category id and must not be sent as one.
    /// </summary>
    [Fact]
    public void ObjectRemoveConfirm_WritesFlagIdAndBothStrings()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(ObjectRemoveConfirmMessageComposer),
            new ObjectRemoveConfirmMessageComposer
            {
                IsWallItem = 1,
                ObjectId = 4711,
                ConfirmTitle = "Remove?",
                ConfirmBody = "This cannot be undone.",
            }
        );

        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(4711);
        packet.PopString().Should().Be("Remove?");
        packet.PopString().Should().Be("This cannot be undone.");
    }

    [Fact]
    public void ObjectRemoveConfirm_FloorItemSendsZeroNotTheCategory()
    {
        ClientPacket packet = SerializeAndReadBody(
            typeof(ObjectRemoveConfirmMessageComposer),
            new ObjectRemoveConfirmMessageComposer
            {
                IsWallItem = 0,
                ObjectId = 1,
                ConfirmTitle = "t",
                ConfirmBody = "b",
            }
        );

        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(1);
    }
}
