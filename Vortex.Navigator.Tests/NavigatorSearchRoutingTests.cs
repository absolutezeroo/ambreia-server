using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Navigator.Tests;

/// <summary>
/// The navigator answers a handful of things: my rooms, my friends' rooms, my groups' bases, every
/// guild base, a text search, and the tab overviews built from those. Anything else a client or an
/// old configuration row still sends (popular, official, history, ...) answers nothing at all --
/// not the whole hotel, which is what the old fall-through did.
/// </summary>
public sealed class NavigatorSearchRoutingTests
{
    private static readonly PlayerId Player = (PlayerId)7;

    [Theory]
    [InlineData(NavigatorSearchCodes.MyRooms, nameof(INavigatorProvider.GetRoomsByOwnerAsync))]
    [InlineData(NavigatorSearchCodes.FriendsRooms, nameof(INavigatorProvider.GetFriendsRoomsAsync))]
    [InlineData(
        NavigatorSearchCodes.MyGuildBases,
        nameof(INavigatorProvider.GetMyGuildBaseRoomsAsync)
    )]
    [InlineData(NavigatorSearchCodes.GuildBases, nameof(INavigatorProvider.GetGuildBaseRoomsAsync))]
    public async Task SearchCode_RunsItsOwnQuery(string searchCode, string expectedProviderCall)
    {
        Recorder recorder = new();

        await SearchAsync(recorder, searchCode).ConfigureAwait(true);

        recorder.Calls.Should().Equal(expectedProviderCall);
    }

    [Fact]
    public async Task TextSearch_RunsTheFreeTextQuery()
    {
        Recorder recorder = new();

        await SearchAsync(recorder, NavigatorSearchCodes.TextSearch, filterValue: "lounge")
            .ConfigureAwait(true);

        recorder.Calls.Should().Equal(nameof(INavigatorProvider.SearchRoomsAsync));
    }

    /// <summary>A text search with nothing typed is not "every room".</summary>
    [Fact]
    public async Task TextSearch_WithNoText_ReturnsNothing()
    {
        Recorder recorder = new();

        ImmutableArray<RoomInfoSnapshot> results = await SearchAsync(
                recorder,
                NavigatorSearchCodes.TextSearch
            )
            .ConfigureAwait(true);

        results.Should().BeEmpty();
        recorder.Calls.Should().BeEmpty();
    }

    /// <summary>Typing in the search box sends a value with no prefix; it is a text search whatever
    /// tab it was typed on.</summary>
    [Fact]
    public async Task UnprefixedFilterValue_BecomesATextSearch()
    {
        Recorder recorder = new();

        await SearchAsync(recorder, NavigatorSearchCodes.MyRooms, filterValue: "lounge")
            .ConfigureAwait(true);

        recorder.Calls.Should().Equal(nameof(INavigatorProvider.SearchRoomsAsync));
    }

    [Fact]
    public async Task PrefixedGroupFilter_StaysAGuildNameSearch()
    {
        Recorder recorder = new();

        await SearchAsync(
                recorder,
                NavigatorSearchCodes.MyRooms,
                NavigatorSearchFilterType.Group,
                "pixel"
            )
            .ConfigureAwait(true);

        recorder.Calls.Should().Equal(nameof(INavigatorProvider.GetRoomsByGroupNameAsync));
    }

    /// <summary>A code the navigator no longer serves, or never did, answers nothing without
    /// throwing -- and above all does not fall back to listing the hotel.</summary>
    [Theory]
    [InlineData("popular")]
    [InlineData("official")]
    [InlineData("history")]
    [InlineData("history_freq")]
    [InlineData("highest_score")]
    [InlineData("recommended")]
    [InlineData("favorites")]
    [InlineData("with_rights")]
    [InlineData("with_friends")]
    [InlineData("staffpicks")]
    [InlineData("new_ads")]
    [InlineData("categories")]
    [InlineData("category__3")]
    [InlineData("eventcategory__4")]
    [InlineData("competition")]
    [InlineData("something_nobody_implemented")]
    public async Task RemovedOrUnknownCode_ReturnsNothing_AndRunsNoQuery(string searchCode)
    {
        Recorder recorder = new();

        ImmutableArray<RoomInfoSnapshot> results = await SearchAsync(recorder, searchCode)
            .ConfigureAwait(true);

        results.Should().BeEmpty();
        recorder.Calls.Should().BeEmpty();
    }

    /// <summary>The same holds one level up, where a tab's overview is assembled from quick links:
    /// a quick link on a removed code is an empty block, not a hotel listing.</summary>
    [Fact]
    public async Task OverviewQuickLinkOnARemovedCode_IsAnEmptyBlock()
    {
        Recorder recorder = new() { QuickLinks = [NavigatorSearchCodes.MyRooms, "popular"] };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> blocks = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.MyWorldView
            )
            .ConfigureAwait(true);

        blocks.Select(b => b.SearchCode).Should().Equal(NavigatorSearchCodes.MyRooms, "popular");
        blocks.SelectMany(b => b.Results).Should().BeEmpty();
        recorder.Calls.Should().Equal(nameof(INavigatorProvider.GetRoomsByOwnerAsync));
    }

    /// <summary>
    /// A tab is an overview. "My World" is my rooms AND my friends' rooms AND my guild bases -- one
    /// collapsible block each. Answering it with a single block is what made every tab show one list.
    /// </summary>
    [Fact]
    public async Task TopLevelView_ReturnsOneBlockPerQuickLink()
    {
        Recorder recorder = new()
        {
            QuickLinks =
            [
                NavigatorSearchCodes.MyRooms,
                NavigatorSearchCodes.FriendsRooms,
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
                NavigatorSearchCodes.FriendsRooms,
                NavigatorSearchCodes.MyGuildBases
            );

        // Each block ran its own query rather than three copies of one.
        recorder
            .Calls.Should()
            .Equal(
                nameof(INavigatorProvider.GetRoomsByOwnerAsync),
                nameof(INavigatorProvider.GetFriendsRoomsAsync),
                nameof(INavigatorProvider.GetMyGuildBaseRoomsAsync)
            );
    }

    /// <summary>The official hotel sends <c>0</c> on every block of a tab overview -- the collapse
    /// arrow is the only affordance there -- and reserves "back" for a drill-down.</summary>
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
    /// quick link row, so they have to travel onto the block.</summary>
    [Fact]
    public async Task OverviewBlock_CarriesTheQuickLinksLabelAndCollapsedDefault()
    {
        Recorder recorder = new()
        {
            QuickLinks = [NavigatorSearchCodes.MyRooms],
            QuickLinkLocalization = "My houses",
            QuickLinkForceClosed = true,
        };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> overview = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.MyWorldView
            )
            .ConfigureAwait(true);

        overview.Should().ContainSingle();
        overview[0].Text.Should().Be("My houses");
        overview[0].ForceClosed.Should().BeTrue();
    }

    /// <summary>Typing a filter is a question, not a tab: the overview collapses to one list of
    /// matches.</summary>
    [Fact]
    public async Task TopLevelViewWithAFilter_CollapsesToASingleSearchBlock()
    {
        Recorder recorder = new()
        {
            QuickLinks = [NavigatorSearchCodes.MyRooms, NavigatorSearchCodes.FriendsRooms],
        };

        ImmutableArray<NavigatorSearchResultBlockSnapshot> blocks = await BlocksAsync(
                recorder,
                NavigatorSearchCodes.MyWorldView,
                filterValue: "lounge"
            )
            .ConfigureAwait(true);

        blocks.Should().ContainSingle();
        recorder.Calls.Should().Equal(nameof(INavigatorProvider.SearchRoomsAsync));
    }

    private static Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> BlocksAsync(
        Recorder recorder,
        string searchCode,
        string filterValue = ""
    ) =>
        BuildService(recorder)
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
        string filterValue = ""
    ) =>
        BuildService(recorder)
            .GetSearchResultsAsync(
                searchCode,
                filterType,
                filterValue,
                Player,
                CancellationToken.None
            );

    private static NavigatorService BuildService(Recorder recorder) =>
        new(
            NullLogger<INavigatorService>.Instance,
            recorder.AsProvider(),
            FakeProxy.Create<IRoomService>(_ => null),
            BuildGrainFactory(),
            FakeProxy.Create<IVortexMetrics>(_ => null)
        );

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

            return FakeProxy.CreateFor(grainType, _ => null);
        });

    /// <summary>
    /// Records which provider query the router picked. Every room-returning method answers with an
    /// empty list, so a test only ever asserts on the routing decision. Query-type resolution is the
    /// real provider's, so what is routed is what an unconfigured hotel actually resolves.
    /// </summary>
    private sealed class Recorder
    {
        // ResolveQueryType reads cached reference data, never the database, so the factory is never
        // touched -- but the constructor still wants one.
        private static readonly NavigatorProvider RealProvider = new(
            FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
            NullLogger<NavigatorProvider>.Instance
        );

        public List<string> Calls { get; } = [];

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
                        return RealProvider.ResolveQueryType((string)call.Args![0]!);

                    case nameof(INavigatorProvider.GetTopLevelContextsAsync):
                        return Task.FromResult(BuildContexts());
                }

                Calls.Add(call.Method.Name);

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
                                            ForceClosed = QuickLinkForceClosed,
                                        }
                                ),
                            ],
                        }
                    ),
                ];
    }
}
