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
using Vortex.Protocol.Messages.Outgoing.NewNavigator;

namespace Vortex.PacketHandlers.Navigator;

/// <summary>
///     Shared orchestration for the navigator search handlers that just resolve a fixed
///     search code to results and send back a single-block response (guilds, room ads,
///     official rooms, friends' rooms, rooms with rights).
/// </summary>
internal static class NavigatorSearchHandlerHelper
{
    public static async ValueTask SendSimpleSearchResultsAsync(
        INavigatorService navigatorService,
        IGrainFactory grainFactory,
        string searchCode,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        ImmutableArray<RoomInfoSnapshot> results = await navigatorService
            .GetSearchResultsAsync(
                searchCode,
                NavigatorSearchFilterType.Anything,
                string.Empty,
                ctx.PlayerId,
                ct
            )
            .ConfigureAwait(false);

        int viewMode = await grainFactory
            .GetPlayerNavigatorGrain(ctx.PlayerId)
            .GetViewModeAsync(searchCode, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new NavigatorSearchResultBlocksMessageComposer
                {
                    SearchCodeOriginal = searchCode,
                    FilteringData = string.Empty,
                    Blocks =
                    [
                        new NavigatorSearchResultBlockSnapshot
                        {
                            SearchCode = searchCode,
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
