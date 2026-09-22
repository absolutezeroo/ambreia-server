using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Outgoing.NewNavigator;

namespace Vortex.PacketHandlers.Navigator;

public class RoomTextSearchMessageHandler(
    INavigatorService navigatorService,
    IGrainFactory grainFactory
) : IMessageHandler<RoomTextSearchMessage>
{
    private const string SearchCode = NavigatorSearchCodes.TextSearch;

    private readonly INavigatorService _navigatorService = navigatorService;
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        RoomTextSearchMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        string query = message.Query ?? string.Empty;

        // The client prefixes this query itself ("group:", "roomname:", ...) depending on which
        // quick-search the player used, so it has to be split here too — passing it through raw
        // would search room names for the literal text "group:Foo".
        (NavigatorSearchFilterType filterType, string filterValue) = NavigatorSearchFilter.Parse(
            query
        );

        ImmutableArray<RoomInfoSnapshot> results = await _navigatorService
            .GetSearchResultsAsync(SearchCode, filterType, filterValue, ctx.PlayerId, ct)
            .ConfigureAwait(false);

        int viewMode = await _grainFactory
            .GetPlayerNavigatorGrain(ctx.PlayerId)
            .GetViewModeAsync(SearchCode, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new NavigatorSearchResultBlocksMessageComposer
                {
                    SearchCodeOriginal = SearchCode,
                    FilteringData = query,
                    Blocks =
                    [
                        new NavigatorSearchResultBlockSnapshot
                        {
                            SearchCode = SearchCode,
                            Text = string.Empty,
                            ActionAllowed = NavigatorActionAllowedType.Back,
                            Localization = string.Empty,
                            ForceClosed = false,
                            ViewMode = (NavigatorViewModeType)viewMode,
                            Results = results,
                        },
                    ],
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
