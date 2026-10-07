using System;
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
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Moderation;

/// <summary>
/// The two mutes a room enforces, and why they are two.
/// </summary>
/// <remarks>
/// A room mute is the room owner's rule and stops at their door. The mod tool's mute is a sanction
/// on the person and must not: applying it as a room mute meant a muted player got their voice back
/// by walking next door, which is the whole reason the hotel-scoped one exists. These lock in that
/// the room reads both, that the longer one wins, and that neither can quietly clear the other.
/// </remarks>
public sealed class HotelMuteEnforcementTests
{
    private const int ROOM_ID = 77;
    private static readonly PlayerId Talker = new(501);

    [Fact]
    public void AHotelMute_SilencesAPlayerThisRoomNeverMuted()
    {
        (RoomChatSystem chat, RoomGrain grain) = NewChat();

        grain._state.HotelMuteExpiresUtc[Talker] = DateTime.UtcNow.AddMinutes(10);

        chat.IsUserMuted(Talker, out int secondsRemaining).Should().BeTrue();
        secondsRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NoMuteOfEitherKind_LeavesThePlayerAbleToSpeak()
    {
        (RoomChatSystem chat, RoomGrain _) = NewChat();

        chat.IsUserMuted(Talker, out int secondsRemaining).Should().BeFalse();
        secondsRemaining.Should().Be(0);
    }

    [Fact]
    public void AnExpiredHotelMute_StopsSilencingAndIsRetired()
    {
        (RoomChatSystem chat, RoomGrain grain) = NewChat();

        grain._state.HotelMuteExpiresUtc[Talker] = DateTime.UtcNow.AddSeconds(-1);

        chat.IsUserMuted(Talker, out _).Should().BeFalse();

        // Retired on read so the table does not keep growing with sanctions that ended.
        grain._state.HotelMuteExpiresUtc.Should().NotContainKey(Talker);
    }

    [Fact]
    public void TheLongerOfTheTwoWins_WhenTheHotelSanctionOutlastsTheRoomRule()
    {
        (RoomChatSystem chat, RoomGrain grain) = NewChat();

        grain._state.MuteExpiresUtc[Talker] = DateTime.UtcNow.AddMinutes(1);
        grain._state.HotelMuteExpiresUtc[Talker] = DateTime.UtcNow.AddMinutes(30);

        chat.IsUserMuted(Talker, out int secondsRemaining).Should().BeTrue();

        // Reporting the room's one minute would tell the player they are nearly free when they have
        // half an hour of a staff sanction left to serve.
        secondsRemaining.Should().BeGreaterThan(60);
    }

    [Fact]
    public void AnExpiredRoomMute_DoesNotLiftAStandingHotelSanction()
    {
        (RoomChatSystem chat, RoomGrain grain) = NewChat();

        grain._state.MuteExpiresUtc[Talker] = DateTime.UtcNow.AddSeconds(-1);
        grain._state.HotelMuteExpiresUtc[Talker] = DateTime.UtcNow.AddMinutes(10);

        chat.IsUserMuted(Talker, out _).Should().BeTrue();
        grain._state.HotelMuteExpiresUtc.Should().ContainKey(Talker);
    }

    [Fact]
    public void AStandingRoomMute_StillSilencesWithNoHotelSanction()
    {
        (RoomChatSystem chat, RoomGrain grain) = NewChat();

        grain._state.MuteExpiresUtc[Talker] = DateTime.UtcNow.AddMinutes(5);

        chat.IsUserMuted(Talker, out _).Should().BeTrue();
    }

    private static (RoomChatSystem Chat, RoomGrain Grain) NewChat()
    {
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
            FakeProxy.Create<IGrainFactory>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => null),
            RoomGrainStubs.NeverCancels(),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            FakeProxy.Create<IRoomModerationStore>(_ => null),
            TestRoomGames.Provider(),
            FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
            RoomGrainStubs.PassThroughWordFilter()
        );

        return (new RoomChatSystem(grain), grain);
    }
}
