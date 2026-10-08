using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Availability;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Revisions;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Production;

public sealed class ReleaseRevisionTests
{
    private static readonly Rev Dev = new();

    // Every name renumbered 1, 2, 3... in name order, the two fixed ones kept on 4000: a valid table.
    private static ReleaseTable Table(Func<Dictionary<string, int>, Dictionary<string, int>>? edit = null)
    {
        static Dictionary<string, int> Renumber(IReadOnlyDictionary<string, int> dev, string fixedName) =>
            dev.Keys.Order(StringComparer.Ordinal)
                .Select((name, i) => (name, id: name == fixedName ? ReleaseTable.FixedHeader : i + 1))
                .ToDictionary(e => e.name, e => e.id);

        Dictionary<string, int> events = Renumber(DevHeaders.Events, "ClientHelloMessageEvent");
        Dictionary<string, int> composers = Renumber(DevHeaders.Composers, "DisconnectReasonComposer");

        return new ReleaseTable("AMBREIA-202610081530", edit?.Invoke(events) ?? events, composers);
    }

    private static string ComposerName(int devHeader) => DevHeaders.Composers.Single(e => e.Value == devHeader).Key;

    [Fact]
    public void Revision_IsTheTablesName()
    {
        new ReleaseRevision(Table()).Revision.Should().Be("AMBREIA-202610081530");
    }

    [Fact]
    public void EveryDevParser_AnswersUnderItsReleaseNumber()
    {
        ReleaseTable table = Table();
        ReleaseRevision release = new(table);

        foreach ((string name, int devId) in DevHeaders.Events.Where(e => Dev.Parsers.ContainsKey(e.Value)))
        {
            release.Parsers[table.Events[name]].Should().BeOfType(Dev.Parsers[devId].GetType(), name);
        }

        release.Parsers.Should().HaveCount(Dev.Parsers.Count);
    }

    [Fact]
    public void EveryDevSerializer_IsRenumberedToItsReleaseNumber()
    {
        ReleaseTable table = Table();
        ReleaseRevision release = new(table);

        release.Serializers.Keys.Should().BeEquivalentTo(Dev.Serializers.Keys);

        foreach ((Type type, ISerializer devSerializer) in Dev.Serializers)
        {
            release.Serializers[type].Header.Should().Be(table.Composers[ComposerName(devSerializer.Header)], type.Name);
        }
    }

    [Fact]
    public void ARenumberedPacket_ChangesBytesFourAndFive_AndNothingElse()
    {
        ReleaseRevision release = new(Table());
        MaintenanceStatusMessageComposer status = new()
        {
            IsInMaintenance = true,
            MinutesUntilMaintenance = 5,
            DurationMinutes = 30,
        };
        Type type = typeof(MaintenanceStatusMessageComposer);

        byte[] devBytes = Dev.Serializers[type].Serialize(status).ToArray();
        byte[] releaseBytes = release.Serializers[type].Serialize(status).ToArray();

        releaseBytes.Length.Should().Be(devBytes.Length);
        releaseBytes.Take(4).Should().Equal(devBytes.Take(4), "the length prefix is unchanged");
        ((releaseBytes[4] << 8) | releaseBytes[5]).Should().Be(release.Serializers[type].Header);
        ((releaseBytes[4] << 8) | releaseBytes[5]).Should().NotBe(Dev.Serializers[type].Header);
        releaseBytes.Skip(6).Should().Equal(devBytes.Skip(6), "the payload is unchanged");
    }

    [Fact]
    public void DisconnectReason_IsByteForByteTheDevOne()
    {
        DisconnectReasonEventMessageComposer composer = new() { Reason = 122 };
        Type type = typeof(DisconnectReasonEventMessageComposer);

        new ReleaseRevision(Table()).Serializers[type].Serialize(composer).ToArray()
            .Should().Equal(Dev.Serializers[type].Serialize(composer).ToArray(), "it keeps 4000 in every revision");
    }

    [Fact]
    public void AMissingMessage_IsNamed()
    {
        Action build = () => new ReleaseRevision(Table(events => { events.Remove("SSOTicketMessageEvent"); return events; }));

        build.Should().Throw<InvalidOperationException>().WithMessage("*SSOTicketMessageEvent*");
    }

    [Fact]
    public void AMovedFixedHeader_IsRefused()
    {
        Action build = () => new ReleaseRevision(Table(events => { events["ClientHelloMessageEvent"] = 17; return events; }));

        build.Should().Throw<InvalidOperationException>().WithMessage("*ClientHelloMessageEvent*4000*");
    }

    [Fact]
    public void ASharedNumber_IsRefused()
    {
        Action build = () => new ReleaseRevision(Table(events =>
        {
            events["SSOTicketMessageEvent"] = events["PongMessageEvent"];
            return events;
        }));

        build.Should().Throw<InvalidOperationException>().WithMessage("*share*");
    }

    [Fact]
    public void ANumberOutsideTheRange_IsRefused()
    {
        Action build = () => new ReleaseRevision(Table(events => { events["SSOTicketMessageEvent"] = 4001; return events; }));

        build.Should().Throw<InvalidOperationException>().WithMessage("*SSOTicketMessageEvent*1..3999*");
    }

    [Fact]
    public void Parse_ReadsTheJsonFormat()
    {
        ReleaseTable table = ReleaseTable.Parse("""
            {"revision":"AMBREIA-202610081530","events":{"A":1},"composers":{"B":2}}
            """);

        table.Revision.Should().Be("AMBREIA-202610081530");
        table.Events.Should().Equal(new Dictionary<string, int> { ["A"] = 1 });
        table.Composers.Should().Equal(new Dictionary<string, int> { ["B"] = 2 });
    }
}
