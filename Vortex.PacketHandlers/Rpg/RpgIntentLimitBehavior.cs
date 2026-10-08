using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Pipeline.Attributes;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.PacketHandlers.Rpg;

/// <summary>
/// One character-screen intent per interval and socket (spec F3a §5.5). A refused create, class
/// choice or entry is answered <c>rate_limited</c> so the screen does not wait; a refused list
/// is dropped, the one on screen is still right.
/// </summary>
[Order(int.MinValue + 2)]
public sealed class RpgIntentLimitBehavior(
    RpgIntentLimiter limiter,
    IGrainFactory grainFactory,
    IVortexMetrics metrics,
    ILogger<RpgIntentLimitBehavior> logger
) : IMessageBehavior<IAccountPhaseMessage>
{
    public async ValueTask InvokeAsync(
        IAccountPhaseMessage env,
        MessageContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        if (limiter.TryConsume(ctx.SessionKey, env))
        {
            await next().ConfigureAwait(false);

            return;
        }

        metrics.RpgPacketRefused("rate_limited");
        logger.LogDebug(
            "Rate-limited {MessageType} from session {SessionKey}",
            env.GetType().Name,
            ctx.SessionKey
        );

        int? action = env switch
        {
            RpgCharacterCreateMessage => RpgCharacterResultMessageComposer.ActionCreate,
            RpgCharacterChooseClassMessage => RpgCharacterResultMessageComposer.ActionChooseClass,
            RpgEnterWorldMessage => RpgCharacterResultMessageComposer.ActionEnterWorld,
            _ => null,
        };

        if (action is not { } a || ctx.AccountId <= 0)
        {
            return;
        }

        // Caught here: a behavior that throws is skipped by the pipeline, which would hand the
        // refused packet to its handler.
        try
        {
            await grainFactory
                .GetAccountSessionGrain(ctx.AccountId)
                .SendComposerAsync(
                    new RpgCharacterResultMessageComposer
                    {
                        Action = a,
                        Code = "rate_limited",
                        PlayerId = 0,
                    }
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not tell session {SessionKey} it was rate-limited",
                ctx.SessionKey
            );
        }
    }
}
