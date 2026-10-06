using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class MyGuildBasesSearchMessageHandler(INavigatorService navigatorService)
    : IMessageHandler<MyGuildBasesSearchMessage>
{
    private const string SearchCode = NavigatorSearchCodes.MyGuildBases;

    public ValueTask HandleAsync(
        MyGuildBasesSearchMessage message,
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
