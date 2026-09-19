using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Help;
using Vortex.Protocol.Messages.Outgoing.Callforhelp;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Help;

/// <summary>
///     Header 1834 in, 3809 out, and 3028 for the appeal the window's own button sends. The
///     reply's eleven-field record is read by WIN63's <c>_SafeCls_2648</c> ctor, whose three
///     trailing appeal fields gate the whole appeal half of the window.
/// </summary>
public sealed class MyCfhReportStatusWireTests
{
    private const int GetMyCfhReportStatusMessageEvent = 1834;
    private const int AppealCfhMessageEvent = 3028;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket Body(MyCfhReportStatusMessageComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(MyCfhReportStatusMessageComposer)]
            .Serialize(composer)
            .ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    private static CfhReportStatusSnapshot Report(long closeTime) =>
        new()
        {
            Id = 12,
            CreationTime = 1_756_000_000_000,
            Message = "he keeps flooding",
            TopicId = 34,
            ReportedAccountName = "Ctuto",
            CloseTime = closeTime,
            Sanctioned = false,
            AppealStatus = CfhAppealStatus.None,
            AppealCreationTime = -1,
            AppealResolutionTime = -1,
        };

    [Fact]
    public void Request_ParsesWithNoBody()
    {
        Revision
            .Parsers[GetMyCfhReportStatusMessageEvent]
            .Parse(new ClientPacket(GetMyCfhReportStatusMessageEvent, Array.Empty<byte>()))
            .Should()
            .BeOfType<GetMyCfhReportStatusMessage>();
    }

    [Fact]
    public void Appeal_ParsesTheReportId()
    {
        // MyReportStatus.as:250 sends the report's own id and nothing else.
        byte[] body = [0, 0, 0, 12];
        ClientPacket packet = new(AppealCfhMessageEvent, body);

        Revision
            .Parsers[AppealCfhMessageEvent]
            .Parse(packet)
            .Should()
            .BeOfType<AppealCfhMessage>()
            .Which.IssueId.Should()
            .Be(12);
    }

    [Fact]
    public void AppealedReport_CarriesItsStatusAndDatesRatherThanTheHardcodedNone()
    {
        // The three trailing fields were written as constants until appeals existed. A snapshot
        // carrying a real appeal has to reach the wire, or the window keeps printing "decided" on
        // a report the player has already contested.
        ClientPacket packet = Body(
            new MyCfhReportStatusMessageComposer
            {
                Reports =
                [
                    Report(closeTime: 1_756_000_500_000) with
                    {
                        AppealStatus = CfhAppealStatus.ActionTaken,
                        AppealCreationTime = 1_756_000_600_000,
                        AppealResolutionTime = 1_756_000_700_000,
                        Sanctioned = true,
                    },
                ],
            }
        );

        packet.PopInt().Should().Be(1);
        packet.PopLong().Should().Be(12);
        packet.PopLong().Should().Be(1_756_000_000_000);
        packet.PopString().Should().Be("he keeps flooding");
        packet.PopInt().Should().Be(34);
        packet.PopString().Should().Be("Ctuto");
        packet.PopLong().Should().Be(1_756_000_500_000);
        packet.PopBoolean().Should().BeTrue();
        packet.PopBoolean().Should().BeFalse();
        packet.PopByte().Should().Be((byte)CfhAppealStatus.ActionTaken);
        packet.PopLong().Should().Be(1_756_000_600_000);
        packet.PopLong().Should().Be(1_756_000_700_000);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void EmptyHistory_IsACountOfZero()
    {
        // Still worth sending: the window is built from the reply, so no reply is no window.
        ClientPacket packet = Body(new MyCfhReportStatusMessageComposer { Reports = [] });

        packet.PopInt().Should().Be(0);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void OpenReport_WritesTheElevenFieldsWithMinusOneForEveryDateItDoesNotHave()
    {
        ClientPacket packet = Body(
            new MyCfhReportStatusMessageComposer { Reports = [Report(closeTime: -1)] }
        );

        packet.PopInt().Should().Be(1);
        packet.PopLong().Should().Be(12);
        packet.PopLong().Should().Be(1_756_000_000_000);
        packet.PopString().Should().Be("he keeps flooding");
        packet.PopInt().Should().Be(34);
        packet.PopString().Should().Be("Ctuto");
        // -1, not 0: the client tests this exact value to tell "pending" from "decided", and a 0
        // would date the decision to 1970 on a report nobody has picked up yet.
        packet.PopLong().Should().Be(-1);
        packet.PopBoolean().Should().BeFalse();
        packet.PopBoolean().Should().BeFalse();
        packet.PopByte().Should().Be(0);
        packet.PopLong().Should().Be(-1);
        packet.PopLong().Should().Be(-1);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void TwoReports_PackBackToBackWithNothingBetweenThem()
    {
        // The record is fixed-width apart from its two strings, so a field written short or long
        // only shows up as the second record decoding into nonsense.
        ClientPacket packet = Body(
            new MyCfhReportStatusMessageComposer
            {
                Reports =
                [
                    Report(closeTime: 1_756_000_500_000) with
                    {
                        Sanctioned = true,
                    },
                    Report(closeTime: -1) with
                    {
                        Id = 13,
                        ReportedAccountName = string.Empty,
                    },
                ],
            }
        );

        packet.PopInt().Should().Be(2);

        packet.PopLong().Should().Be(12);
        packet.PopLong().Should().Be(1_756_000_000_000);
        packet.PopString().Should().Be("he keeps flooding");
        packet.PopInt().Should().Be(34);
        packet.PopString().Should().Be("Ctuto");
        packet.PopLong().Should().Be(1_756_000_500_000);
        packet.PopBoolean().Should().BeTrue();
        packet.PopBoolean().Should().BeFalse();
        packet.PopByte().Should().Be(0);
        packet.PopLong().Should().Be(-1);
        packet.PopLong().Should().Be(-1);

        packet.PopLong().Should().Be(13);
        packet.PopLong().Should().Be(1_756_000_000_000);
        packet.PopString().Should().Be("he keeps flooding");
        packet.PopInt().Should().Be(34);
        // A room report names nobody; the client prints its own "Deleted" placeholder for this.
        packet.PopString().Should().BeEmpty();
        packet.PopLong().Should().Be(-1);
        packet.PopBoolean().Should().BeFalse();
        packet.PopBoolean().Should().BeFalse();
        packet.PopByte().Should().Be(0);
        packet.PopLong().Should().Be(-1);
        packet.PopLong().Should().Be(-1);

        packet.Remaining.Should().Be(0);
    }
}
