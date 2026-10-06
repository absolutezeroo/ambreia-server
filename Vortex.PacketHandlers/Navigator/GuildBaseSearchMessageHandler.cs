using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class GuildBaseSearchMessageHandler(INavigatorService navigatorService)
    : IMessageHandler<GuildBaseSearchMessage>
{
    private const string SearchCode = NavigatorSearchCodes.GuildBases;

    public ValueTask HandleAsync(
        GuildBaseSearchMessage message,
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
