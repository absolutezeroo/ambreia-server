using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Chat;

/// <summary>
/// The hotel-wide word filter on room chat: it is asked as RoomChat for the speaker, what it
/// replaces is what the room hears, and a refusal reaches nobody but tells the speaker why.
/// </summary>
public sealed class HotelWordFilterTests
{
    private const int ROOM_ID = 93;
    private static readonly PlayerId Talker = new(501);

    private readonly List<object> _sentToTalker = [];
    private readonly List<ProxyCall> _filterCalls = [];

    [Fact]
    public async Task AReplacedLine_ComesBackReplaced()
    {
        RoomChatSystem chat = NewChat(new WordFilterResult("t'es bobba", false, true));

        (await chat.ApplyHotelFilterAsync(Talker, "t'es con")).Should().Be("t'es bobba");

        _filterCalls
            .Should()
            .ContainSingle(c => c.Method.Name == nameof(IWordFilter.Apply))
            .Which.Args.Should()
            .Equal("t'es con", WordFilterSource.RoomChat, 501);
        _sentToTalker.Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusedLine_IsNull_AndTheSpeakerGetsTheBubble()
    {
        RoomChatSystem chat = NewChat(new WordFilterResult("t'es con", true, false));

        (await chat.ApplyHotelFilterAsync(Talker, "t'es con")).Should().BeNull();

        NotificationDialogMessageComposer bubble = _sentToTalker
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<NotificationDialogMessageComposer>()
            .Subject;
        bubble.Type.Should().Be(WordFilterNotice.Type);
        bubble.Parameters.Should().Contain(p => p.Key == "message" && p.Value == "Interdit.");
    }

    private RoomChatSystem NewChat(WordFilterResult answer)
    {
        IWordFilter filter = FakeProxy.Create<IWordFilter>(call =>
        {
            _filterCalls.Add(call);

            return call.Method.Name switch
            {
                nameof(IWordFilter.Apply) => answer,
                "get_RefusedMessage" => "Interdit.",
                _ => null,
            };
        });

        IPlayerPresenceGrain presence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
        {
            if (call.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
            {
                _sentToTalker.Add(call.Args![0]!);
            }

            return null;
        });

        IGrainFactory grains = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
            && call.Method.IsGenericMethod
            && call.Method.GetGenericArguments()[0] == typeof(IPlayerPresenceGrain)
                ? presence
                : null
        );

        RoomGrain grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            ROOM_ID,
            FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IStuffDataFactory>(_ => null),
            Options.Create(new RoomConfig()),
            NullLogger<IRoomGrain>.Instance,
            FakeProxy.Create<IRoomModelProvider>(_ => null),
            FakeProxy.Create<IRoomItemsProvider>(_ => null),
            FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
            FakeProxy.Create<IRoomAvatarProvider>(_ => null),
            RoomGrainStubs.NoListeners(),
            grains,
            FakeProxy.Create<IEventPublisher>(_ => null),
            RoomGrainStubs.NeverCancels(),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            FakeProxy.Create<IRoomModerationStore>(_ => null),
            TestRoomGames.Provider(),
            FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
            filter
        );

        return new RoomChatSystem(grain);
    }
}
