using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

/// <summary>Answers with an empty block on purpose: no room-competition subsystem exists, and
/// falling back to "all rooms" advertised the whole hotel as entrants. See
/// <see cref="Primitives.Navigator.Enums.NavigatorQueryType.Competition"/>.</summary>
public class CompetitionRoomsSearchMessageHandler(
    INavigatorService navigatorService,
    IGrainFactory grainFactory
) : IMessageHandler<CompetitionRoomsSearchMessage>
{
    private const string SearchCode = NavigatorSearchCodes.Competition;

    public ValueTask HandleAsync(
        CompetitionRoomsSearchMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        NavigatorSearchHandlerHelper.SendSimpleSearchResultsAsync(
            navigatorService,
            grainFactory,
            SearchCode,
            ctx,
            ct
        );
}
