using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Chat;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Protocol.Messages.Incoming.Rpg.Chat;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec F3c §4: a chat message is one call to the player's presence; nothing without a
/// player.</summary>
public sealed class ChatSendHandlerTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task AMessage_GoesToThePlayersPresence()
    {
        await HandleAsync(playerId: 7);

        _calls.Should().Equal("presence 7: whisper Bob salut");
    }

    [Fact]
    public async Task WithoutAPlayer_NothingIsCalled()
    {
        await HandleAsync(playerId: 0);

        _calls.Should().BeEmpty();
    }

    private Task HandleAsync(int playerId) =>
        new RpgChatSendMessageHandler(Grains())
            .HandleAsync(
                new RpgChatSendMessage
                {
                    Channel = "whisper",
                    Target = "Bob",
                    Text = "salut",
                },
                new MessageContext(
                    FakeProxy.Create<ISessionContext>(_ => null),
                    new PlayerId(playerId),
                    new RoomId(12)
                ),
                CancellationToken.None
            )
            .AsTask();

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                {
                    if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendChatAsync))
                    {
                        _calls.Add(
                            $"presence {(long)call.Args![0]!}: {inner.Args![0]} {inner.Args[1]} {inner.Args[2]}"
                        );
                    }

                    return Task.CompletedTask;
                })
                : null
        );
}
