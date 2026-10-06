using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Sound.Snapshots;
using Vortex.Protocol.Messages.Incoming.Sound;
using Vortex.Protocol.Messages.Outgoing.Sound;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Sound;

/// <summary>
/// Locks the Trax byte contract against the official client's own parsers. Every one of these
/// messages was an empty stub: the parsers dropped their fields and the serializers wrote nothing,
/// so a hotel could ship a full song catalogue and the client would still show untitled disks it
/// refused to play.
/// </summary>
public sealed class SoundWireLayoutTests
{
    private const int GetSongInfoEvent = 3130;
    private const int GetOfficialSongIdEvent = 1723;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket BuildClientPacket(int header, Action<ServerPacket> write)
    {
        ServerPacket sp = new(header);
        write(sp);
        return new ClientPacket(header, sp.ToArray());
    }

    private static ClientPacket SerializeAndReadBody(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        // AbstractSerializer prepends int length (4) + short header (2).
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    [Fact]
    public void GetSongInfoParser_ReadsTheCountThenThatManyIds()
    {
        // HabboMusicController::sendNextSongRequestMessage() sends the pending array as
        // length-then-ids, once a second.
        ClientPacket packet = BuildClientPacket(
            GetSongInfoEvent,
            sp =>
            {
                sp.WriteInteger(3);
                sp.WriteInteger(11);
                sp.WriteInteger(22);
                sp.WriteInteger(33);
            }
        );

        GetSongInfoMessage message = Revision
            .Parsers[GetSongInfoEvent]
            .Parse(packet)
            .Should()
            .BeOfType<GetSongInfoMessage>()
            .Subject;

        message.SongIds.Should().Equal(11, 22, 33);
    }

    [Fact]
    public void GetSongInfoParser_CannotBeMadeToAllocateOnAnOversizedCount()
    {
        // A count of a billion with four bytes behind it. What is left in the packet is the ceiling,
        // so this reads one id and stops rather than sizing a buffer from the claim.
        ClientPacket packet = BuildClientPacket(
            GetSongInfoEvent,
            sp =>
            {
                sp.WriteInteger(1_000_000_000);
                sp.WriteInteger(7);
            }
        );

        GetSongInfoMessage message = (GetSongInfoMessage)
            Revision.Parsers[GetSongInfoEvent].Parse(packet);

        message.SongIds.Should().Equal(7);
    }

    [Fact]
    public void GetOfficialSongIdParser_ReadsTheCode()
    {
        ClientPacket packet = BuildClientPacket(
            GetOfficialSongIdEvent,
            sp => sp.WriteString("radio_zero_01")
        );

        GetOfficialSongIdMessage message = Revision
            .Parsers[GetOfficialSongIdEvent]
            .Parse(packet)
            .Should()
            .BeOfType<GetOfficialSongIdMessage>()
            .Subject;

        message.OfficialSongId.Should().Be("radio_zero_01");
    }

    [Fact]
    public void TraxSongInfoSerializer_WritesTheSixFieldsInTheClientsReadOrder()
    {
        // _SafeCls_2898.parse(): count, then per song id, code, name, data, length, creator. The
        // code is read and discarded by the client, and the field is still part of the layout.
        ClientPacket body = SerializeAndReadBody(
            typeof(TraxSongInfoMessageComposer),
            new TraxSongInfoMessageComposer
            {
                Songs =
                [
                    new SongSnapshot
                    {
                        Id = 274,
                        Name = "Tapes from Goa",
                        Creator = "Sulake",
                        LengthMs = 128_000,
                        OfficialSongId = "goa_01",
                        Data = "0:0:1;",
                    },
                ],
            }
        );

        body.PopInt().Should().Be(1);
        body.PopInt().Should().Be(274);
        body.PopString().Should().Be("goa_01");
        body.PopString().Should().Be("Tapes from Goa");
        body.PopString().Should().Be("0:0:1;");
        body.PopInt().Should().Be(128_000);
        body.PopString().Should().Be("Sulake");
        body.Remaining.Should().Be(0);
    }

    [Fact]
    public void UserSongDisksSerializer_WritesDiskThenSong()
    {
        // _SafeCls_3657.parse(): count, then pairs added as (key, value) — the client keys the map
        // by the disk and looks the song up by the value. Swapping them makes every disk report the
        // wrong song, with no error anywhere.
        ClientPacket body = SerializeAndReadBody(
            typeof(UserSongDisksInventoryMessageComposer),
            new UserSongDisksInventoryMessageComposer
            {
                Disks = [new SongDiskSnapshot { DiskId = 9001, SongId = 274 }],
            }
        );

        body.PopInt().Should().Be(1);
        body.PopInt().Should().Be(9001);
        body.PopInt().Should().Be(274);
        body.Remaining.Should().Be(0);
    }

    [Fact]
    public void PlayListSerializer_WritesFourFieldsPerSong_NotTraxSongInfosSix()
    {
        ClientPacket body = SerializeAndReadBody(
            typeof(PlayListMessageComposer),
            new PlayListMessageComposer
            {
                SynchronizationCountMs = 0,
                Songs =
                [
                    new SongSnapshot
                    {
                        Id = 274,
                        Name = "Tapes from Goa",
                        Creator = "Sulake",
                        LengthMs = 128_000,
                        OfficialSongId = "goa_01",
                        Data = "0:0:1;",
                    },
                ],
            }
        );

        body.PopInt().Should().Be(0);
        body.PopInt().Should().Be(1);
        body.PopInt().Should().Be(274);
        body.PopInt().Should().Be(128_000);
        body.PopString().Should().Be("Tapes from Goa");
        body.PopString().Should().Be("Sulake");
        body.Remaining.Should().Be(0);
    }

    [Fact]
    public void OfficialSongIdSerializer_WritesTheCodeThenTheId()
    {
        ClientPacket body = SerializeAndReadBody(
            typeof(OfficialSongIdMessageComposer),
            new OfficialSongIdMessageComposer { OfficialSongId = "goa_01", SongId = 274 }
        );

        body.PopString().Should().Be("goa_01");
        body.PopInt().Should().Be(274);
        body.Remaining.Should().Be(0);
    }
}
