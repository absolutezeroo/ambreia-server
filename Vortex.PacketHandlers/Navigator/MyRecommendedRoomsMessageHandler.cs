using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class MyRecommendedRoomsMessageHandler(
    INavigatorService navigatorService,
    IGrainFactory grainFactory
) : IMessageHandler<MyRecommendedRoomsMessage>
{
    private const string SearchCode = NavigatorSearchCodes.Recommended;

    public ValueTask HandleAsync(
        MyRecommendedRoomsMessage message,
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
