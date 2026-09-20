using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public class UnignoreUserMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<UnignoreUserMessage>
{
    // IgnoredUsersManager.onIgnoreResult() case 3: the only thing that ever takes a player back
    // off the client's ignore list. Without it the unignore reached the database and nothing
    // else -- the avatar menu kept offering "unignore", the muted marker stayed on the avatar,
    // and clicking the row a second time looked like a dead button.
    private const int UnignoredResultCode = 3;

    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        UnignoreUserMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        IMessengerGrain grain = _grainFactory.GetMessengerGrain(ctx.PlayerId);
        await grain.UnignoreUserAsync(PlayerId.Parse(message.UserId), ct).ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new IgnoreResultMessageComposer
                {
                    UserId = message.UserId,
                    ResultCode = UnignoredResultCode,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
