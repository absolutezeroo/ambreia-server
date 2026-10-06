using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Navigator;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Navigator.Tests;

/// <summary>
/// The promoted-rooms strip above the official rooms list.
/// </summary>
/// <remarks>
/// The rule the whole packet rests on: <b>a group with no rooms must never be emitted</b>. The
/// client's parser reads a group's headline room before its loop and outside the count, so an empty
/// group leaves it decoding a block that was never written — and everything after it in the packet,
/// the other groups and the official entries, arrives as garbage. A configured group whose search
/// happens to find nothing is therefore dropped rather than sent.
/// </remarks>
public sealed class NavigatorPromotedRoomsTests
{
    private const string FilledCode = "friendlyRoom";
    private const string EmptyCode = "partyRoom";

    [Fact]
    public async Task AGroupWhoseSearchFindsNothing_IsNotEmitted()
    {
        ImmutableArray<PromotedRoomCategorySnapshot> groups = await BuildService(
                (NavigatorSearchCodes.StaffPicks, 2),
                (NavigatorSearchCodes.Popular, 0)
            )
            .GetPromotedRoomsAsync((PlayerId)1, CancellationToken.None)
            .ConfigureAwait(true);

        groups.Select(g => g.Code).Should().Equal(FilledCode);
    }

    [Fact]
    public async Task AGroupWithRooms_CarriesThemInOrder()
    {
        ImmutableArray<PromotedRoomCategorySnapshot> groups = await BuildService(
                (NavigatorSearchCodes.StaffPicks, 3),
                (NavigatorSearchCodes.Popular, 0)
            )
            .GetPromotedRoomsAsync((PlayerId)1, CancellationToken.None)
            .ConfigureAwait(true);

        groups.Should().HaveCount(1);
        groups[0].Rooms.Select(r => r.RoomId.Value).Should().Equal(1, 2, 3);
    }

    /// <summary>The client's own expander is labelled "view top ten", so a group that carried more
    /// would disagree with the button that opens it.</summary>
    [Fact]
    public async Task AGroupIsCappedAtTheTopTen()
    {
        ImmutableArray<PromotedRoomCategorySnapshot> groups = await BuildService(
                (NavigatorSearchCodes.StaffPicks, 25),
                (NavigatorSearchCodes.Popular, 0)
            )
            .GetPromotedRoomsAsync((PlayerId)1, CancellationToken.None)
            .ConfigureAwait(true);

        groups[0].Rooms.Should().HaveCount(NavigatorPromotedCategoryCodes.GroupSize);
    }

    /// <summary>The avatar beside a group is the headline room's owner, resolved per request. A
    /// figure stored on the row would be wrong the first time that player changed clothes.</summary>
    [Fact]
    public async Task TheLeaderFigure_IsTheHeadlineRoomOwners()
    {
        ImmutableArray<PromotedRoomCategorySnapshot> groups = await BuildService(
                (NavigatorSearchCodes.StaffPicks, 2),
                (NavigatorSearchCodes.Popular, 0)
            )
            .GetPromotedRoomsAsync((PlayerId)1, CancellationToken.None)
            .ConfigureAwait(true);

        groups[0].LeaderFigure.Should().Be("figure-of-owner-101");
    }

    [Fact]
    public async Task NoConfiguredGroups_SendsNothingRatherThanAnEmptyStrip()
    {
        ImmutableArray<PromotedRoomCategorySnapshot> groups = await BuildService()
            .GetPromotedRoomsAsync((PlayerId)1, CancellationToken.None)
            .ConfigureAwait(true);

        groups.Should().BeEmpty();
    }

    private static NavigatorService BuildService(
        params (string SearchCode, int RoomCount)[] configured
    ) =>
        new(
            NullLogger<INavigatorService>.Instance,
            BuildProvider(configured),
            FakeProxy.Create<IRoomService>(_ => null),
            BuildGrainFactory(),
            FakeProxy.Create<IVortexMetrics>(_ => null)
        );

    private static INavigatorProvider BuildProvider((string SearchCode, int RoomCount)[] configured)
    {
        // Which provider method a group reaches is decided by its search code, so the room counts
        // are keyed by the method the code resolves to rather than by the code itself.
        Dictionary<string, int> countByMethod = configured.ToDictionary(
            entry =>
                entry.SearchCode == NavigatorSearchCodes.StaffPicks
                    ? nameof(INavigatorProvider.GetStaffPickedRoomsAsync)
                    : nameof(INavigatorProvider.GetAllRoomsAsync),
            entry => entry.RoomCount,
            StringComparer.Ordinal
        );

        ImmutableArray<NavigatorPromotedCategorySnapshot> categories =
        [
            .. configured.Select(
                (entry, index) =>
                    new NavigatorPromotedCategorySnapshot
                    {
                        Id = index + 1,
                        Code =
                            entry.SearchCode == NavigatorSearchCodes.StaffPicks
                                ? FilledCode
                                : EmptyCode,
                        SearchCode = entry.SearchCode,
                        Filter = string.Empty,
                    }
            ),
        ];

        return FakeProxy.Create<INavigatorProvider>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(INavigatorProvider.GetPromotedCategories):
                    return categories;

                case nameof(INavigatorProvider.ResolveQueryType):
                    return NavigatorSearchCodes.QueryTypeBySearchCode.GetValueOrDefault(
                        (string)call.Args![0]!,
                        NavigatorQueryType.AllRooms
                    );

                case nameof(INavigatorProvider.GetPlayerFiguresAsync):
                    return Task.FromResult(
                        ((IReadOnlyCollection<int>)call.Args![0]!).ToImmutableDictionary(
                            id => id,
                            id => $"figure-of-owner-{id}"
                        )
                    );
            }

            return call.Method.ReturnType == typeof(Task<List<RoomInfoSnapshot>>)
                ? Task.FromResult(Rooms(countByMethod.GetValueOrDefault(call.Method.Name)))
                : null;
        });
    }

    private static List<RoomInfoSnapshot> Rooms(int count) =>
        [.. Enumerable.Range(1, count).Select(NewRoom)];

    private static RoomInfoSnapshot NewRoom(int id) =>
        new()
        {
            RoomId = id,
            Name = $"Room {id}",
            Description = string.Empty,
            OwnerId = (PlayerId)(100 + id),
            OwnerName = $"owner{id}",
            Population = 0,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Score = 0,
            Ranking = 0,
            CategoryId = -1,
            Tags = [],
            StaffPick = true,
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            LastUpdatedUtc = DateTime.UnixEpoch,
        };

    private static IGrainFactory BuildGrainFactory() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (call.Method.Name != nameof(IGrainFactory.GetGrain))
            {
                return null;
            }

            Type grainType = call.Method.GetGenericArguments()[0];

            if (grainType == typeof(IServerConfigGrain))
            {
                return FakeProxy.Create<IServerConfigGrain>(config =>
                    config.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                        ? Task.FromResult((int)config.Args![1]!)
                        : null
                );
            }

            return grainType == typeof(IRoomDirectoryGrain)
                ? FakeProxy.Create<IRoomDirectoryGrain>(directory =>
                    directory.Method.Name == nameof(IRoomDirectoryGrain.GetActiveRoomsAsync)
                        // A default ImmutableArray is not an empty one; it throws on use.
                        ? Task.FromResult(ImmutableArray<RoomSummarySnapshot>.Empty)
                        : null
                )
                : null;
        });
}
