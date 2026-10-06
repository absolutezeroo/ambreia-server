using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Help;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Help;

/// <summary>
///     Only the room-based report was ever parsed; the other entry points produced empty messages,
///     so reporting from an IM or a guild forum reached the server as nothing at all. (The photo and
///     selfie variants were removed in M0 bis.)
///
///     These also end with the reporter's own name and email (`help_message_name` /
///     `help_message_email`, TopicsFlowHelpController.as:478-483), filled only for the client's
///     unlawful report categories. Those two were dropped until 2026-09-15: the AS3 composers build
///     their payload with an array literal (`= [p1..p7]`) rather than pushes, so the spec scanner
///     recorded "0 fields, partial" and the field-count gate skipped them.
/// </summary>
public sealed class CallForHelpVariantWireTests
{
    private const int CallForHelpFromIMMessageEvent = 838;
    private const int CallForHelpFromForumThreadMessageEvent = 380;
    private const int CallForHelpFromForumMessageMessageEvent = 2991;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket BuildClientPacket(int header, Action<ServerPacket> write)
    {
        ServerPacket sp = new(header);
        write(sp);
        return new ClientPacket(header, sp.ToArray());
    }

    private static T Parse<T>(int header, Action<ServerPacket> write)
        where T : class =>
        Revision
            .Parsers[header]
            .Parse(BuildClientPacket(header, write))
            .Should()
            .BeOfType<T>()
            .Subject;

    [Fact]
    public void FromImParser_ReadsTheEvidenceLinesAsUserIdAndTextPairs()
    {
        CallForHelpFromIMMessage message = Parse<CallForHelpFromIMMessage>(
            CallForHelpFromIMMessageEvent,
            sp =>
                sp.WriteString("they keep messaging me")
                    .WriteInteger(4)
                    .WriteInteger(77)
                    .WriteInteger(2)
                    .WriteInteger(77)
                    .WriteString("go away")
                    .WriteInteger(77)
                    .WriteString("seriously")
                    .WriteString("Ada")
                    .WriteString("ada@example.test")
        );

        message.Message.Should().Be("they keep messaging me");
        message.TopicId.Should().Be(4);
        message.ReportedUserId.Should().Be(77);
        message.Evidence.Should().HaveCount(2);
        message.Evidence[0].UserId.Should().Be(77);
        message.Evidence[0].Text.Should().Be("go away");
        message.Evidence[1].Text.Should().Be("seriously");
        message.ReporterName.Should().Be("Ada");
        message.ReporterEmail.Should().Be("ada@example.test");
    }

    [Fact]
    public void FromImParser_AcceptsAReportWithNoEvidenceSelected()
    {
        CallForHelpFromIMMessage message = Parse<CallForHelpFromIMMessage>(
            CallForHelpFromIMMessageEvent,
            sp =>
                sp.WriteString("no context")
                    .WriteInteger(4)
                    .WriteInteger(77)
                    .WriteInteger(0)
                    .WriteString("")
                    .WriteString("")
        );

        message.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void FromForumThreadParser_ReadsTheGuildThreadTopicAndMessage()
    {
        CallForHelpFromForumThreadMessage message = Parse<CallForHelpFromForumThreadMessage>(
            CallForHelpFromForumThreadMessageEvent,
            sp =>
                sp.WriteInteger(12)
                    .WriteInteger(34)
                    .WriteInteger(9)
                    .WriteString("this whole thread is abusive")
                    .WriteString("Ada")
                    .WriteString("ada@example.test")
        );

        message.GroupId.Should().Be(12);
        message.ThreadId.Should().Be(34);
        message.TopicId.Should().Be(9);
        message.Message.Should().Be("this whole thread is abusive");
        message.ReporterName.Should().Be("Ada");
        message.ReporterEmail.Should().Be("ada@example.test");
    }

    [Fact]
    public void FromForumMessageParser_ReadsThePostIdBetweenTheThreadAndTheTopic()
    {
        CallForHelpFromForumMessageMessage message = Parse<CallForHelpFromForumMessageMessage>(
            CallForHelpFromForumMessageMessageEvent,
            sp =>
                sp.WriteInteger(12)
                    .WriteInteger(34)
                    .WriteInteger(56)
                    .WriteInteger(9)
                    .WriteString("this reply is abusive")
                    .WriteString("Ada")
                    .WriteString("ada@example.test")
        );

        message.GroupId.Should().Be(12);
        message.ThreadId.Should().Be(34);
        message.PostId.Should().Be(56);
        message.TopicId.Should().Be(9);
        message.Message.Should().Be("this reply is abusive");
        message.ReporterName.Should().Be("Ada");
        message.ReporterEmail.Should().Be("ada@example.test");
    }
}
