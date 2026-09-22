using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.Snapshots.FriendList;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Navigator.Tests;

/// <summary>
/// <c>NavigatorService</c> only ever switched on six of the query types. Everything else — history,
/// rooms with rights, friends' rooms, top rated, recommended, staff picks, text search — fell into
/// the default branch and came back as "every room in the hotel", ordered by population. Each case
/// here pins one tab to the query it is supposed to run.
/// </summary>
public sealed class NavigatorSearchRoutingTests
{
    private static readonly PlayerId Player = (PlayerId)7;

    [Theory]
    [InlineData(NavigatorSearchCodes.MyRooms, nameof(INavigatorProvider.GetRoomsByOwnerAsync))]
    [InlineData(NavigatorSearchCodes.Favourites, nameof(INavigatorProvider.GetFavoriteRoomsAsync))]
    [InlineData(NavigatorSearchCodes.FriendsRooms, nameof(INavigatorProvider.GetFriendsRoomsAsync))]
    [InlineData(NavigatorSearchCodes.History, nameof(INavigatorProvider.GetVisitedRoomsAsync))]
    [InlineData(
        NavigatorSearchCodes.HistoryFrequent,
        nameof(INavigatorProvider.GetVisitedRoomsAsync)
    )]
    [InlineData(
        NavigatorSearchCodes.WithRights,
        nameof(INavigatorProvider.GetRoomsWithRightsAsync)
    )]
    [InlineData(NavigatorSearchCodes.RoomAds, nameof(INavigatorProvider.GetAdvertisedRoomsAsync))]
    [InlineData(
        NavigatorSearchCodes.HighestScore,
        nameof(INavigatorProvider.GetHighestScoreRoomsAsync)
    )]
    [InlineData(
        NavigatorSearchCodes.Recommended,
        nameof(INavigatorProvider.GetRecommendedRoomsAsync)
    )]
    [InlineData(
        NavigatorSearchCodes.StaffPicks,
        nameof(INavigatorProvider.GetStaffPickedRoomsAsync)
    )]
    [InlineData(NavigatorSearchCodes.Official, nameof(INavigatorProvider.GetStaffPickedRoomsAsync))]
    [InlineData(NavigatorSearchCodes.GuildBases, nameof(INavigatorProvider.GetGuildBaseRoomsAsync))]
    [InlineData(
        NavigatorSearchCodes.MyGuildBases,
        nameof(INavigatorProvider.GetMyGuildBaseRoomsAsync)
    )]
    [InlineData("eventcategory__4", nameof(INavigatorProvider.GetRoomsByEventCategoryAsync))]
    public async Task SearchCode_RunsItsOwnQuery(string searchCode, string expectedProviderCall)
    {
        Recorder recorder = new();

        await SearchAsync(recorder, searchCode).ConfigureAwait(true);

        recorder.Calls.Should().Contain(expectedProviderCall);
        recorder
            .Calls.Should()
            .NotContain(
                nameof(INavigatorProvider.GetAllRoomsAsync),
                "falling through to every room in the hotel is the bug this guards"
            );
    }

    /// <summary>"Popular" genuinely is the whole hotel ordered by population, so it is the one tab
    /// that may use the unfiltered query.</summary>
    [Fact]
    public async Task PopularSearch_UsesTheUnfilteredPopularityQuery()
    {
        Recorder recorder = new();

        await SearchAsync(recorder, NavigatorSearchCodes.Popular).ConfigureAwait(true);

        recorder.Calls.Should().Contain(nameof(INavigatorProvider.GetAllRoomsAsync));
    }

    [Fact]
    public async Task HistoryAndFrequentHistory_DifferOnlyByTheirOrderingFlag()
    {
        Recorder recent = new();
        Recorder frequent = new();

        await SearchAsync(recent, NavigatorSearchCodes.History).ConfigureAwait(true);
        await SearchAsync(frequent, NavigatorSearchCodes.HistoryFrequent).ConfigureAwait(true);

        recent.VisitedByFrequency.Should().BeFalse();
        frequent.VisitedByFrequency.Should().BeTrue();
    }

    /// <summary>A competition tab with no competition subsystem must answer "nothing", not
    /// "everything".</summary>
    [Fact]
    public async Task CompetitionSearch_ReturnsNothingRatherThanTheWholeHotel()
    {
        Recorder recorder = new();

        ImmutableArray<RoomInfoSnapshot> results = await SearchAsync(
                recorder,
                NavigatorSearchCodes.Competition
            )
            .ConfigureAwait(true);

        results.Should().BeEmpty();
        recorder.Calls.Should().BeEmpty();
    }

    /// <summary>A category quick link carries the category it means; the router used to ignore it
    /// and ask for category 0 every time.</summary>
    [Fact]
    public async Task CategorySearch_AsksForTheCategoryTheCodeNames()
    {
        Recorder recorder = new();

        await SearchAsync(recorder, "categories", filterValue: "3").ConfigureAwait(true);

        recorder.Calls.Should().Contain(nameof(INavigatorProvider.GetRoomsByCategoryAsync));
        recorder.CategoryId.Should().Be(3);
    }

    [Fact]
    public async Task CategorySearch_WithNoResolvableCategory_ReturnsNothing()
    {
        Recorder recorder = new();

        ImmutableArray<RoomInfoSnapshot> results = await SearchAsync(recorder, "categories")
            .ConfigureAwait(true);

        results.Should().BeEmpty();
        recorder.Calls.Should().NotContain(nameof(INavigatorProvider.GetRoomsByCategoryAsync));
    }

    /// <summary>Typing in the search box sends a value with no prefix; it is a text search whatever
    /// tab it was typed on, not a reason to list the hotel.</summary>
    [Fact]
    public async Task UnprefixedFilterValue_BecomesATextSearch()
    {
        Recorder recorder = new();

        await SearchAsync(recorder, NavigatorSearchCodes.Popular, filterValue: "lounge")
            .ConfigureAwait(true);

        recorder.Calls.Should().Contain(nameof(INavigatorProvider.SearchRoomsAsync));
        recorder.Calls.Should().NotContain(nameof(INavigatorProvider.GetAllRoomsAsync));
    }

    [Fact]
    public async Task PrefixedGroupFilter_StaysAGuildNameSearch()
    {
        Recorder recorder = new();

        await SearchAsync(
                recorder,
                NavigatorSearchCodes.Popular,
                NavigatorSearchFilterType.Group,
                "pixel"
            )
            .ConfigureAwait(true);

        recorder.Calls.Should().Contain(nameof(INavigatorProvider.GetRoomsByGroupNameAsync));
    }

    /// <summary>Nobody online, nobody to follow: the tab must not fall back to a room list.</summary>
    [Fact]
    public async Task RoomsWithFriends_WithNoOnlineFriends_ReturnsNothing()
    {
        Recorder recorder = new();

        ImmutableArray<RoomInfoSnapshot> results = await SearchAsync(
                recorder,
                NavigatorSearchCodes.WithFriends
            )
            .ConfigureAwait(true);

        results.Should().BeEmpty();
        recorder.Calls.Should().NotContain(nameof(INavigatorProvider.GetAllRoomsAsync));
    }

    [Fact]
    public async Task RoomsWithFriends_LooksUpTheRoomsOnlineFriendsAreIn()
    {
        Recorder recorder = new();

        await SearchAsync(
                recorder,
                NavigatorSearchCodes.WithFriends,
                onlineFriendIds: [11, 12, 13],
                friendRoomIds: [55, 55, 66]
            )
            .ConfigureAwait(true);

        recorder.Calls.Should().Contain(nameof(INavigatorProvider.GetRoomsByIdsAsync));
        // Two friends in the same room is one room to show, not two.
        recorder.RequestedRoomIds.Should().BeEquivalentTo([55, 66]);
    }

    /// <summary>
    /// A tab is an overview. "My World" is my rooms AND my favourites AND my history AND the rooms I
    /// hold rights in AND my guild bases -- one collapsible block each. Answering it with a single
    /// block is what made every tab show one list.
    /// </summary>
    [Fact]
    public async Task TopLevelView_ReturnsOneBlockPerQuickLink()
    {
        Recorder recorder = new()
        {
            QuickLinks =
            [
                NavigatorSearchCodes.MyRooms,
                NavigatorSearchCodes.Favourites,
                NavigatorSearchCodes.History,
                NavigatorSearchCodes.WithRights,
                NavigatorSearchCodes.MyGuildBases,
            ],
        };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> blocks = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.MyWorldView
            )
            .ConfigureAwait(true);

        blocks
            .Select(b => b.SearchCode)
            .Should()
            .Equal(
                NavigatorSearchCodes.MyRooms,
                NavigatorSearchCodes.Favourites,
                NavigatorSearchCodes.History,
                NavigatorSearchCodes.WithRights,
                NavigatorSearchCodes.MyGuildBases
            );

        // Each block ran its own query rather than five copies of one.
        recorder
            .Calls.Should()
            .Contain([
                nameof(INavigatorProvider.GetRoomsByOwnerAsync),
                nameof(INavigatorProvider.GetFavoriteRoomsAsync),
                nameof(INavigatorProvider.GetVisitedRoomsAsync),
                nameof(INavigatorProvider.GetRoomsWithRightsAsync),
                nameof(INavigatorProvider.GetMyGuildBaseRoomsAsync),
            ]);
    }

    /// <summary>The official hotel sends <c>0</c> on every block of a tab overview -- the collapse
    /// arrow is the only affordance there -- and reserves "back" for a drill-down. "Show more" on an
    /// overview block re-searches that code with no filter, so it answers with a different list than
    /// the one on screen.</summary>
    [Fact]
    public async Task OverviewBlocksOfferNoAction_AndADrillDownOffersBack()
    {
        Recorder overviewRecorder = new() { QuickLinks = [NavigatorSearchCodes.MyRooms] };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> overview = await BlocksAsync(
                overviewRecorder,
                NavigatorSearchCodes.MyWorldView
            )
            .ConfigureAwait(true);

        ImmutableArray<NavigatorSearchResultBlockSnapshot> drillDown = await BlocksAsync(
                new Recorder(),
                NavigatorSearchCodes.MyRooms
            )
            .ConfigureAwait(true);

        overview.Should().ContainSingle();
        overview[0].ActionAllowed.Should().Be(NavigatorActionAllowedType.None);

        drillDown.Should().ContainSingle();
        drillDown[0].ActionAllowed.Should().Be(NavigatorActionAllowedType.Back);
    }

    /// <summary>The client shows a block header as <c>text</c> when it is non-empty and otherwise
    /// localizes the search code, and starts the block closed on <c>forceClosed</c>. Both live on the
    /// quick link row, so an operator who fills them gets nothing unless they travel onto the block.
    /// </summary>
    [Fact]
    public async Task OverviewBlock_CarriesTheQuickLinksLabelAndCollapsedDefault()
    {
        Recorder recorder = new()
        {
            QuickLinks = [NavigatorSearchCodes.MyRooms],
            QuickLinkLocalization = "Staff picks",
            QuickLinkForceClosed = true,
        };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> overview = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.MyWorldView
            )
            .ConfigureAwait(true);

        overview.Should().ContainSingle();
        overview[0].Text.Should().Be("Staff picks");
        overview[0].ForceClosed.Should().BeTrue();
    }

    /// <summary>Typing a filter is a question, not a tab: the overview collapses to one list of
    /// matches.</summary>
    [Fact]
    public async Task TopLevelViewWithAFilter_CollapsesToASingleSearchBlock()
    {
        Recorder recorder = new()
        {
            QuickLinks = [NavigatorSearchCodes.MyRooms, NavigatorSearchCodes.Favourites],
        };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> blocks = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.MyWorldView,
                filterValue: "lounge"
            )
            .ConfigureAwait(true);

        blocks.Should().ContainSingle();
        recorder.Calls.Should().Contain(nameof(INavigatorProvider.SearchRoomsAsync));
    }

    /// <summary>"All categories" is itself a set of blocks, so it expands in place instead of
    /// collapsing into one nameless list.</summary>
    [Fact]
    public async Task CategoriesQuickLink_ExpandsIntoOneBlockPerCategory()
    {
        Recorder recorder = new()
        {
            QuickLinks = [NavigatorSearchCodes.Popular, NavigatorSearchCodes.Categories],
        };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> blocks = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.HotelView
            )
            .ConfigureAwait(true);

        blocks
            .Select(b => b.SearchCode)
            .Should()
            .Equal(NavigatorSearchCodes.Popular, NavigatorSearchCodes.FlatCategoryCode(3));

        // A category block carries its own header text, and a per-category code so collapsing one
        // does not collapse them all.
        blocks[1].Text.Should().Be("${navigator.flatcategory.global.PARTY}");
    }

    private static Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> BlocksAsync(
        Recorder recorder,
        string searchCode,
        string filterValue = ""
    ) =>
        BuildService(recorder, [], [])
            .GetSearchBlocksAsync(
                searchCode,
                NavigatorSearchFilterType.Anything,
                filterValue,
                Player,
                CancellationToken.None
            );

    private static Task<ImmutableArray<RoomInfoSnapshot>> SearchAsync(
        Recorder recorder,
        string searchCode,
        NavigatorSearchFilterType filterType = NavigatorSearchFilterType.Anything,
        string filterValue = "",
        IReadOnlyList<int>? onlineFriendIds = null,
        IReadOnlyList<int>? friendRoomIds = null
    ) =>
        BuildService(recorder, onlineFriendIds ?? [], friendRoomIds ?? [])
            .GetSearchResultsAsync(
                searchCode,
                filterType,
                filterValue,
                Player,
                CancellationToken.None
            );

    private static NavigatorService BuildService(
        Recorder recorder,
        IReadOnlyList<int> onlineFriendIds,
        IReadOnlyList<int> friendRoomIds
    ) =>
        new(
            NullLogger<INavigatorService>.Instance,
            recorder.AsProvider(),
            FakeProxy.Create<IRoomService>(_ => null),
            BuildGrainFactory(onlineFriendIds, friendRoomIds),
            FakeProxy.Create<IVortexMetrics>(_ => null)
        );

    private static IGrainFactory BuildGrainFactory(
        IReadOnlyList<int> onlineFriendIds,
        IReadOnlyList<int> friendRoomIds
    )
    {
        // Presence grains are keyed per player, so hand each requested key its own room from the
        // scripted list rather than answering every friend with the same one.
        int presenceIndex = 0;

        return FakeProxy.Create<IGrainFactory>(call =>
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
                        // Whatever default the caller passes is the value under test conditions.
                        ? Task.FromResult((int)config.Args![1]!)
                        : null
                );
            }

            if (grainType == typeof(IRoomDirectoryGrain))
            {
                return FakeProxy.Create<IRoomDirectoryGrain>(directory =>
                    directory.Method.Name == nameof(IRoomDirectoryGrain.GetActiveRoomsAsync)
                        // A default ImmutableArray is not an empty one; it throws on use.
                        ? Task.FromResult(ImmutableArray<RoomSummarySnapshot>.Empty)
                        : null
                );
            }

            if (grainType == typeof(IMessengerGrain))
            {
                return FakeProxy.Create<IMessengerGrain>(messenger =>
                    messenger.Method.Name == nameof(IMessengerGrain.GetFriendsAsync)
                        ? Task.FromResult(onlineFriendIds.Select(BuildOnlineFriend).ToList())
                        : null
                );
            }

            if (grainType == typeof(IPlayerPresenceGrain))
            {
                int roomId = presenceIndex < friendRoomIds.Count ? friendRoomIds[presenceIndex] : 0;

                presenceIndex++;

                return FakeProxy.Create<IPlayerPresenceGrain>(presence =>
                    presence.Method.Name == nameof(IPlayerPresenceGrain.GetActiveRoomAsync)
                        ? Task.FromResult(
                            new RoomPointerSnapshot
                            {
                                RoomId = (RoomId)roomId,
                                ActiveSinceUtc = DateTime.UnixEpoch,
                            }
                        )
                        : null
                );
            }

            // Anything else (the per-player navigator grain and its view modes) answers with its
            // type's default, which is what an untouched preference is anyway.
            return FakeProxy.CreateFor(grainType, _ => null);
        });
    }

    private static MessengerFriendSnapshot BuildOnlineFriend(int playerId) =>
        new()
        {
            PlayerId = (PlayerId)playerId,
            Name = $"friend{playerId}",
            Gender = AvatarGenderType.Male,
            Online = true,
            FollowingAllowed = true,
            Figure = string.Empty,
            CategoryId = 0,
            Motto = string.Empty,
            RealName = string.Empty,
            FacebookId = string.Empty,
            PersistedMessageUser = false,
            VipMember = false,
            PocketHabboUser = false,
            RelationshipStatus = 0,
        };

    /// <summary>
    /// Records which provider query the router picked. Every room-returning method answers with an
    /// empty list, so a test only ever asserts on the routing decision.
    /// </summary>
    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
        public bool? VisitedByFrequency { get; private set; }
        public int? CategoryId { get; private set; }
        public IReadOnlyCollection<int>? RequestedRoomIds { get; private set; }

        /// <summary>Quick links configured under every top-level context this fake knows about.
        /// Empty means "no navigator configuration", the unseeded-hotel case.</summary>
        public IReadOnlyList<string> QuickLinks { get; init; } = [];

        /// <summary>The label an operator typed on every quick link this fake hands out.</summary>
        public string QuickLinkLocalization { get; init; } = string.Empty;

        /// <summary>Whether those quick links are configured to start collapsed.</summary>
        public bool QuickLinkForceClosed { get; init; }

        public INavigatorProvider AsProvider() =>
            FakeProxy.Create<INavigatorProvider>(call =>
            {
                switch (call.Method.Name)
                {
                    case nameof(INavigatorProvider.ResolveQueryType):
                        return Resolve((string)call.Args![0]!);

                    case nameof(INavigatorProvider.GetFlatCategories):
                        return CategoryReferenceData;

                    case nameof(INavigatorProvider.GetTopLevelContextsAsync):
                        return Task.FromResult(BuildContexts());
                }

                Calls.Add(call.Method.Name);

                switch (call.Method.Name)
                {
                    case nameof(INavigatorProvider.GetVisitedRoomsAsync):
                        VisitedByFrequency = (bool)call.Args![1]!;
                        break;

                    case nameof(INavigatorProvider.GetRoomsByCategoryAsync):
                        CategoryId = (int)call.Args![0]!;
                        break;

                    case nameof(INavigatorProvider.GetRoomsByIdsAsync):
                        RequestedRoomIds = (IReadOnlyCollection<int>)call.Args![0]!;
                        break;
                }

                return call.Method.ReturnType == typeof(Task<List<RoomInfoSnapshot>>)
                    ? Task.FromResult(new List<RoomInfoSnapshot>())
                    : null;
            });

        private ImmutableArray<NavigatorTopLevelContextSnapshot> BuildContexts() =>
            QuickLinks.Count == 0
                ? []
                :
                [
                    .. NavigatorSearchCodes.TopLevelViews.Select(
                        view => new NavigatorTopLevelContextSnapshot
                        {
                            SearchCode = view,
                            QueryType = Resolve(view),
                            QuickLinks =
                            [
                                .. QuickLinks.Select(
                                    (code, index) =>
                                        new NavigatorQuickLinkSnapshot
                                        {
                                            Id = index + 1,
                                            SearchCode = code,
                                            Filter = string.Empty,
                                            Localization = QuickLinkLocalization,
                                            QueryType = Resolve(code),
                                            ForceClosed = QuickLinkForceClosed,
                                        }
                                ),
                            ],
                        }
                    ),
                ];

        /// <summary>Mirrors the provider's own fallback so the routing under test is exercised with
        /// the resolution an unconfigured hotel actually gets.</summary>
        private static NavigatorQueryType Resolve(string searchCode) =>
            NavigatorSearchCodes.QueryTypeBySearchCode.TryGetValue(
                searchCode,
                out NavigatorQueryType queryType
            )
                ? queryType
            : searchCode.StartsWith(
                NavigatorSearchCodes.EventCategoryPrefix,
                StringComparison.Ordinal
            )
                ? NavigatorQueryType.EventCategory
            : searchCode.StartsWith(
                NavigatorSearchCodes.FlatCategoryPrefix,
                StringComparison.Ordinal
            )
                ? NavigatorQueryType.ByFlatCategory
            : NavigatorQueryType.AllRooms;

        private static readonly ImmutableArray<NavigatorFlatCategorySnapshot> CategoryReferenceData =
        [
            new()
            {
                Id = 3,
                Name = "Party",
                MinRank = 1,
                Visible = true,
                Automatic = false,
                AutomaticCategory = string.Empty,
                GlobalCategory = "PARTY",
                StaffOnly = false,
            },
        ];
    }
}
