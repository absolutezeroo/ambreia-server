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
public sealed class WordFilterHandlerTests
{
    private readonly List<object> _sent = [];
    private readonly List<ProxyCall> _grainCalls = [];
    private readonly List<ProxyCall> _filterCalls = [];

    [Fact]
    public async Task ARefusedMotto_IsNeverSaved_AndTheBubbleIsSent()
    {
        await new ChangeMottoMessageHandler(Grains(), Refusing()).HandleAsync(
            new ChangeMottoMessage { Text = "con" },
            Context(),
            CancellationToken.None
        );

        _grainCalls.Should().NotContain(c => c.Method.Name == "SetMottoAsync");
        _sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
    }

    [Fact]
    public async Task AReplacedMotto_IsSavedReplaced()
    {
        await new ChangeMottoMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new ChangeMottoMessage { Text = "con" },
            Context(),
            CancellationToken.None
        );

        _grainCalls
            .Should()
            .ContainSingle(c => c.Method.Name == "SetMottoAsync")
            .Which.Args![0]
            .Should()
            .Be("bobba");
        _filterCalls
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

        _grainCalls.Should().NotContain(c => c.Method.Name == "ReceiveRoomInviteAsync");
        _sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
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

        _filterCalls.Should().ContainSingle();
        _grainCalls
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

        _grainCalls.Should().NotContain(c => c.Method.Name == "SendMessageAsync");
        _sent.Should().HaveCount(2);
        _sent.Should().ContainSingle(s => s is NotificationDialogMessageComposer);
        _sent
            .Should()
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

        _grainCalls
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

        _sent
            .Should()
            .ContainSingle()
            .Which.As<CheckUserNameResultMessageComposer>()
            .ResultCode.Should()
            .Be((int)NameChangeResultCode.NameNotValid);
        _grainCalls.Should().NotContain(c => c.Method.Name == "GetPlayerIdAsync");
        _filterCalls.Should().OnlyContain(c => c.Method.Name == nameof(IWordFilter.Test));
    }

    [Fact]
    public async Task AFilteredName_CannotBeClaimed()
    {
        await new ChangeUserNameMessageHandler(Grains(), Config(), Refusing()).HandleAsync(
            new ChangeUserNameMessage { Name = "Connard" },
            Context(),
            CancellationToken.None
        );

        _sent
            .Should()
            .ContainSingle()
            .Which.As<ChangeUserNameResultMessageComposer>()
            .ResultCode.Should()
            .Be((int)NameChangeResultCode.NameNotValid);
        _grainCalls.Should().NotContain(c => c.Method.Name == "SetNameAsync");
        _filterCalls
            .Should()
            .ContainSingle()
            .Which.Args.Should()
            .Equal("Connard", WordFilterSource.Username, 501);
    }

    private MessageContext Context() =>
        new(
            FakeProxy.Create<ISessionContext>(call =>
            {
                if (call.Method.Name == nameof(ISessionContext.SendComposerAsync))
                {
                    _sent.Add(call.Args![0]!);
                }

                return null;
            }),
            PlayerId.Parse(501),
            new RoomId(0)
        );

    // Every grain the handler asks for is a proxy recording its calls into _grainCalls.
    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain) && call.Method.IsGenericMethod
                ? FakeProxy.CreateFor(
                    call.Method.GetGenericArguments()[0],
                    inner =>
                    {
                        _grainCalls.Add(inner);

                        return null;
                    }
                )
                : null
        );

    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    private IWordFilter Refusing() => Filter(text => new WordFilterResult(text, true, false));

    private IWordFilter Replacing(string with) =>
        Filter(_ => new WordFilterResult(with, false, true));

    private IWordFilter Filter(System.Func<string, WordFilterResult> answer) =>
        FakeProxy.Create<IWordFilter>(call =>
        {
            _filterCalls.Add(call);

            return call.Method.Name switch
            {
                nameof(IWordFilter.Apply) or nameof(IWordFilter.Test) => answer(
                    (string)call.Args![0]!
                ),
                "get_RefusedMessage" => "Interdit.",
                _ => null,
            };
        });
}
