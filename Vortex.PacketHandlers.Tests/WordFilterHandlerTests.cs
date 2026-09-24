using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Avatar;
using Vortex.PacketHandlers.FriendList;
using Vortex.PacketHandlers.Room.Avatar;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms;
using Vortex.Protocol.Messages.Incoming.Avatar;
using Vortex.Protocol.Messages.Incoming.FriendList;
using Vortex.Protocol.Messages.Incoming.Room.Avatar;
using Vortex.Protocol.Messages.Outgoing.Avatar;
using Vortex.Protocol.Messages.Outgoing.FriendList;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>
/// The hotel-wide word filter at the five packet handlers that take a player's text: what it refuses
/// reaches no grain, what it replaces is what the grain gets, and the player is told.
/// </summary>
public sealed class WordFilterHandlerTests : WordFilterHandlerFixture
{
    [Fact]
    public async Task ARefusedMotto_IsNeverSaved_AndTheBubbleIsSent()
    {
        await new ChangeMottoMessageHandler(Grains(), Refusing()).HandleAsync(
            new ChangeMottoMessage { Text = "con" },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SetMottoAsync");
        Sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
    }

    [Fact]
    public async Task AReplacedMotto_IsSavedReplaced()
    {
        await new ChangeMottoMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new ChangeMottoMessage { Text = "con" },
            Context(),
            CancellationToken.None
        );

        GrainCalls
            .Should()
            .ContainSingle(c => c.Method.Name == "SetMottoAsync")
            .Which.Args![0]
            .Should()
            .Be("bobba");
        FilterCalls
            .Should()
            .ContainSingle()
            .Which.Args.Should()
            .Equal("con", WordFilterSource.Motto, 501);
    }

    [Fact]
    public async Task ARefusedInvite_ReachesNobody()
    {
        await new SendRoomInviteMessageHandler(
            Grains(),
            Refusing(),
            NullLogger<SendRoomInviteMessageHandler>.Instance
        ).HandleAsync(
            new SendRoomInviteMessage { FriendIds = [7, 8], Message = "con" },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "ReceiveRoomInviteAsync");
        Sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
    }

    [Fact]
    public async Task AReplacedInvite_IsFilteredOnce_AndDeliveredReplaced()
    {
        await new SendRoomInviteMessageHandler(
            Grains(),
            Replacing("bobba"),
            NullLogger<SendRoomInviteMessageHandler>.Instance
        ).HandleAsync(
            new SendRoomInviteMessage { FriendIds = [7, 8], Message = "con" },
            Context(),
            CancellationToken.None
        );

        FilterCalls.Should().ContainSingle();
        GrainCalls
            .FindAll(c => c.Method.Name == "ReceiveRoomInviteAsync")
            .Should()
            .HaveCount(2)
            .And.OnlyContain(c => (string)c.Args![1]! == "bobba");
    }

    [Fact]
    public async Task ARefusedPrivateMessage_IsNeverSent_AndAnswersOfflineFailed()
    {
        await new SendMsgMessageHandler(Grains(), Refusing()).HandleAsync(
            new SendMsgMessage { ChatId = 7, Message = "con" },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SendMessageAsync");
        Sent.Should().HaveCount(2);
        Sent.Should().ContainSingle(s => s is NotificationDialogMessageComposer);
        Sent.Should()
            .ContainSingle(s => s is InstantMessageErrorMessageComposer)
            .Which.As<InstantMessageErrorMessageComposer>()
            .ErrorCode.Should()
            .Be(InstantMessageErrorCodeType.OfflineFailed);
    }

    [Fact]
    public async Task AReplacedPrivateMessage_IsSentReplaced()
    {
        await new SendMsgMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new SendMsgMessage { ChatId = 7, Message = "con" },
            Context(),
            CancellationToken.None
        );

        GrainCalls
            .Should()
            .ContainSingle(c => c.Method.Name == "SendMessageAsync")
            .Which.Args![1]
            .Should()
            .Be("bobba");
    }

    [Fact]
    public async Task AFilteredName_ChecksAsNotValid_WithoutJournaling()
    {
        await new CheckUserNameMessageHandler(Grains(), Config(), Replacing("bobba")).HandleAsync(
            new CheckUserNameMessage { Name = "Connard" },
            Context(),
            CancellationToken.None
        );

        Sent.Should()
            .ContainSingle()
            .Which.As<CheckUserNameResultMessageComposer>()
            .ResultCode.Should()
            .Be((int)NameChangeResultCode.NameNotValid);
        GrainCalls.Should().NotContain(c => c.Method.Name == "GetPlayerIdAsync");
        FilterCalls.Should().OnlyContain(c => c.Method.Name == nameof(IWordFilter.Test));
    }

    [Fact]
    public async Task AFilteredName_CannotBeClaimed()
    {
        await new ChangeUserNameMessageHandler(Grains(), Config(), Refusing()).HandleAsync(
            new ChangeUserNameMessage { Name = "Connard" },
            Context(),
            CancellationToken.None
        );

        Sent.Should()
            .ContainSingle()
            .Which.As<ChangeUserNameResultMessageComposer>()
            .ResultCode.Should()
            .Be((int)NameChangeResultCode.NameNotValid);
        GrainCalls.Should().NotContain(c => c.Method.Name == "SetNameAsync");
        FilterCalls
            .Should()
            .ContainSingle()
            .Which.Args.Should()
            .Equal("Connard", WordFilterSource.Username, 501);
    }
}
