using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Messages.Registry;
using Vortex.Pipeline.Attributes;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;

namespace Vortex.PacketHandlers.Rpg;

/// <summary>
/// Character-screen packets only in the account phase (spec F3a §5.5): an account and no player
/// yet. Game packets need no gate of their own: their handlers already do nothing without a
/// player. Right after the global rate limit, before the per-intent one.
/// </summary>
[Order(int.MinValue + 1)]
public sealed class AccountPhaseGateBehavior(
    IVortexMetrics metrics,
    ILogger<AccountPhaseGateBehavior> logger
) : IMessageBehavior<IAccountPhaseMessage>
{
    public ValueTask InvokeAsync(
        IAccountPhaseMessage env,
        MessageContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        if (ctx.AccountId > 0 && ctx.PlayerId <= 0)
        {
            return next();
        }

        metrics.RpgPacketRefused("phase");
        logger.LogDebug(
            "Dropping {MessageType} from session {SessionKey}: not on the character screen",
            env.GetType().Name,
            ctx.SessionKey
        );

        return ValueTask.CompletedTask;
    }
}
