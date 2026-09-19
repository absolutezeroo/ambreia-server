using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Camera;

/// <summary>
/// The camera's wire, which three composers were describing wrongly.
/// </summary>
/// <remarks>
/// <c>CameraStorageUrl</c>, <c>CameraPublishStatus</c> and <c>CompetitionStatus</c> were all written
/// with empty bodies and a <c>TODO: add properties if/when identified</c>, while the client's parsers
/// read a url, three fields and two fields respectively. A body-less composer is not a neutral
/// placeholder there: the client reads past the end of the message and into whatever followed it.
/// </remarks>
public sealed class CameraWireTests
{
    private const int RenderRoomMessageEvent = 3332;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket Body<TComposer>(TComposer composer)
        where TComposer : Vortex.Primitives.Networking.IComposer
    {
        byte[] bytes = Revision.Serializers[typeof(TComposer)].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    [Fact]
    public void StorageUrl_IsTheOneStringTheClientReads()
    {
        ClientPacket packet = Body(
            new CameraStorageUrlMessageComposer { Url = "photos/ABCDEF.png" }
        );

        packet.PopString().Should().Be("photos/ABCDEF.png");
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void StorageUrl_CarriesAnEmptyStringForAFailure()
    {
        // The client's own failure branch: empty url -> setRenderingFailed() and an alert. It is the
        // only way the wire can say no, so it has to be a real empty string and not no field at all.
        ClientPacket packet = Body(new CameraStorageUrlMessageComposer { Url = string.Empty });

        packet.PopString().Should().BeEmpty();
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void PublishStatus_WritesTheIdOnlyOnSuccess()
    {
        ClientPacket ok = Body(
            new CameraPublishStatusMessageComposer
            {
                Ok = true,
                SecondsToWait = 0,
                ExtraDataId = "ABCDEF",
            }
        );

        ok.PopBoolean().Should().BeTrue();
        ok.PopInt().Should().Be(0);
        ok.PopString().Should().Be("ABCDEF");
        ok.Remaining.Should().Be(0);
    }

    [Fact]
    public void PublishStatus_StopsAfterTheSecondsOnARefusal()
    {
        // The client reads the third field behind `isOk() && bytesAvailable`. Writing an empty
        // string here would be read as a share link the player was told they had.
        ClientPacket refused = Body(
            new CameraPublishStatusMessageComposer
            {
                Ok = false,
                SecondsToWait = 1800,
                ExtraDataId = null,
            }
        );

        refused.PopBoolean().Should().BeFalse();
        refused.PopInt().Should().Be(1800);
        refused.Remaining.Should().Be(0);
    }

    [Fact]
    public void CompetitionStatus_AlwaysWritesBothFields()
    {
        // Unlike the publish status, this string is read unconditionally: a success that stops after
        // the boolean leaves the client reading the next message's bytes as this one's reason.
        ClientPacket ok = Body(
            new CompetitionStatusMessageComposer { Ok = true, ErrorReason = string.Empty }
        );

        ok.PopBoolean().Should().BeTrue();
        ok.PopString().Should().BeEmpty();
        ok.Remaining.Should().Be(0);

        ClientPacket refused = Body(
            new CompetitionStatusMessageComposer { Ok = false, ErrorReason = "too-many-submits" }
        );

        refused.PopBoolean().Should().BeFalse();
        refused.PopString().Should().Be("too-many-submits");
        refused.Remaining.Should().Be(0);
    }

    [Fact]
    public void PurchaseOk_IsGenuinelyBodyLess()
    {
        // The one camera reply that really is empty: the client's parser has no fields and its
        // handler ignores the argument entirely.
        Body(new CameraPurchaseOKMessageComposer()).Remaining.Should().Be(0);
    }

    [Fact]
    public void RenderRoom_ReadsTheLengthPrefixedBlob()
    {
        // A ByteArray argument goes out as an int32 length then the raw bytes, which is what the
        // parser has to mirror — it used to throw the whole body away.
        byte[] payload = [0x78, 0x9C, 0x01, 0x02, 0x03];
        byte[] body = [0, 0, 0, (byte)payload.Length, .. payload];

        RenderRoomMessage parsed = Revision
            .Parsers[RenderRoomMessageEvent]
            .Parse(new ClientPacket(RenderRoomMessageEvent, body))
            .Should()
            .BeOfType<RenderRoomMessage>()
            .Subject;

        parsed.Payload.ToArray().Should().Equal(payload);
    }

    [Fact]
    public void RenderRoom_TreatsALengthLongerThanTheFrameAsNoPayload()
    {
        // The length is the client's. A frame claiming two gigabytes must not be read past its end;
        // an empty payload is refused downstream like any other unusable one.
        byte[] body = [0x7F, 0xFF, 0xFF, 0xFF, 0x01, 0x02];

        RenderRoomMessage parsed = Revision
            .Parsers[RenderRoomMessageEvent]
            .Parse(new ClientPacket(RenderRoomMessageEvent, body))
            .Should()
            .BeOfType<RenderRoomMessage>()
            .Subject;

        parsed.Payload.IsEmpty.Should().BeTrue();
    }
}
