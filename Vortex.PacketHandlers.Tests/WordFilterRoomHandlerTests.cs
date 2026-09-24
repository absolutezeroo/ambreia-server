using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.Catalog;
using Vortex.PacketHandlers.Navigator;
using Vortex.PacketHandlers.RoomSettings;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Incoming.RoomSettings;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>The hotel-wide word filter on rooms: names, descriptions, tags and events.</summary>
public sealed class WordFilterRoomHandlerTests : WordFilterHandlerFixture
{
    private readonly List<ProxyCall> _serviceCalls = [];

    [Fact]
    public async Task ARefusedRoomName_CreatesNothing()
    {
        await new CreateFlatMessageHandler(RoomService(), RefusingOnly("con")).HandleAsync(
            Flat("con", "ok"),
            Context(),
            CancellationToken.None
        );

        _serviceCalls.Should().NotContain(c => c.Method.Name == "CreateRoomAsync");
        Sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
    }

    [Fact]
    public async Task AReplacedRoom_IsCreatedReplaced()
    {
        await new CreateFlatMessageHandler(RoomService(), Replacing("bobba")).HandleAsync(
            Flat("con", "con"),
            Context(),
            CancellationToken.None
        );

        object?[] args = _serviceCalls.Should().ContainSingle().Which.Args!;
        args[0].Should().Be("bobba");
        args[1].Should().Be("bobba");
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.Room);
    }

    [Fact]
    public async Task RefusedRoomSettings_AreNeverSaved()
    {
        await new SaveRoomSettingsMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            new SaveRoomSettingsMessage
            {
                RoomId = new RoomId(4),
                RoomName = "ok",
                RoomDescription = "con",
            },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "UpdateRoomSettingsAsync");
    }

    [Fact]
    public async Task ReplacedRoomSettings_AreSavedReplaced_AndThePasswordIsLeftAlone()
    {
        await new SaveRoomSettingsMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new SaveRoomSettingsMessage
            {
                RoomId = new RoomId(4),
                RoomName = "con",
                RoomDescription = "con",
                Password = "con",
            },
            Context(),
            CancellationToken.None
        );

        RoomSettingsUpdate update = (RoomSettingsUpdate)GrainCall("UpdateRoomSettingsAsync")[1]!;
        update.Name.Should().Be("bobba");
        update.Description.Should().Be("bobba");
        update.Password.Should().Be("con");
    }

    [Fact]
    public async Task ARefusedTag_SetsNoTags_AndAMissingTagIsNotFiltered()
    {
        await new SetRoomSessionTagsMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            new SetRoomSessionTagsMessage { Tag1 = "con", Tag2 = null },
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SetRoomTagsAsync");
        AppliedSources.Should().ContainSingle().Which.Should().Be(WordFilterSource.RoomTag);
    }

    [Fact]
    public async Task CleanTags_AreSet()
    {
        await new SetRoomSessionTagsMessageHandler(Grains(), PassingEverything()).HandleAsync(
            new SetRoomSessionTagsMessage { Tag1 = "fun", Tag2 = "" },
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCall("SetRoomTagsAsync")[1].Should().Be("fun");
    }

    /// <summary>Before the purchase: an event costs credits, and a refused one must not.</summary>
    [Fact]
    public async Task ARefusedRoomAd_IsNeverBought()
    {
        await new PurchaseRoomAdMessageMessageHandler(
            Grains(),
            FakeProxy.Create<INavigatorProvider>(_ => null),
            RefusingOnly("con")
        ).HandleAsync(
            new PurchaseRoomAdMessageMessage
            {
                FlatId = 4,
                Name = "con",
                Description = "ok",
            },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "PurchaseRoomAdAsync");
        Sent.Should().ContainSingle(s => s is NotificationDialogMessageComposer);
        Sent.Should().ContainSingle(s => s is PurchaseErrorMessageComposer);
    }

    [Fact]
    public async Task ARefusedEventEdit_IsNeverSaved()
    {
        await new EditEventMessageHandler(AdService(), RefusingOnly("con")).HandleAsync(
            new EditEventMessage
            {
                Id = 9,
                Name = "ok",
                Description = "con",
            },
            Context(),
            CancellationToken.None
        );

        _serviceCalls.Should().NotContain(c => c.Method.Name == "EditAsync");
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.RoomEvent);
    }

    [Fact]
    public async Task AReplacedEventEdit_IsSavedReplaced()
    {
        await new EditEventMessageHandler(AdService(), Replacing("bobba")).HandleAsync(
            new EditEventMessage
            {
                Id = 9,
                Name = "con",
                Description = "con",
            },
            Context(),
            CancellationToken.None
        );

        object?[] args = _serviceCalls.Should().ContainSingle().Which.Args!;
        args[2].Should().Be("bobba");
        args[3].Should().Be("bobba");
    }

    private static CreateFlatMessage Flat(string name, string description) =>
        new()
        {
            FlatName = name,
            FlatDescription = description,
            FlatModelName = "model_a",
        };

    private IRoomService RoomService() =>
        FakeProxy.Create<IRoomService>(call =>
        {
            _serviceCalls.Add(call);

            return null;
        });

    private IRoomAdvertisementService AdService() =>
        FakeProxy.Create<IRoomAdvertisementService>(call =>
        {
            _serviceCalls.Add(call);

            return null;
        });
}
