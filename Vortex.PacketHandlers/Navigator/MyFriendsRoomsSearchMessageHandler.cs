using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class MyFriendsRoomsSearchMessageHandler(INavigatorService navigatorService)
    : IMessageHandler<MyFriendsRoomsSearchMessage>
{
    private const string SearchCode = NavigatorSearchCodes.FriendsRooms;

    public ValueTask HandleAsync(
        MyFriendsRoomsSearchMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        NavigatorSearchHandlerHelper.SendSimpleSearchResultsAsync(
            navigatorService,
            SearchCode,
            ctx,
            ct
        );
}
