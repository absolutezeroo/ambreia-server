using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.NewNavigator;
using Vortex.Protocol.Messages.Outgoing.NewNavigator;

namespace Vortex.PacketHandlers.NewNavigator;

public class NewNavigatorInitMessageHandler(
    INavigatorService navigatorService,
    IGrainFactory grainFactory
) : IMessageHandler<NewNavigatorInitMessage>
{
    private readonly INavigatorService _navigatorService = navigatorService;
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        NewNavigatorInitMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        IPlayerNavigatorGrain navigatorGrain = _grainFactory.GetPlayerNavigatorGrain(ctx.PlayerId);

        ImmutableArray<NavigatorTopLevelContextSnapshot> topLevelContexts = await _navigatorService
            .GetTopLevelContextAsync()
            .ConfigureAwait(false);

        NavigatorWindowPreferencesSnapshot prefs = await navigatorGrain
            .GetWindowPreferencesAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new NavigatorMetaDataMessage { TopLevelContexts = topLevelContexts },
                ct
            )
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new NavigatorLiftedRoomsMessage { LiftedRooms = [] }, ct)
            .ConfigureAwait(false);

        // The client expects both of these on init, but collapsed categories and saved searches are
        // no longer kept per player: they go out empty (frozen).
        await ctx.SendComposerAsync(
                new NavigatorCollapsedCategoriesMessage { CollapsedCategoryIds = [] },
                ct
            )
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new NavigatorSavedSearchesMessage { SavedSearches = [] }, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new NewNavigatorPreferencesMessageComposer
                {
                    WindowX = prefs.WindowX,
                    WindowY = prefs.WindowY,
                    WindowWidth = prefs.WindowWidth,
                    WindowHeight = prefs.WindowHeight,
                    LeftPaneHidden = prefs.LeftPaneHidden,
                    ResultsMode = prefs.ResultsMode,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
