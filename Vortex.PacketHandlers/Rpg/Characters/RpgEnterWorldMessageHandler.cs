using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Handshake;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.PacketHandlers.Rpg.Characters;

/// <summary>
/// One of the account's characters enters the world by the SSO's own path (spec F3a §5.4). Someone
/// else's character is answered <c>character.not_owned</c> by the grain and the screen stays.
/// </summary>
public class RpgEnterWorldMessageHandler(
    IGrainFactory grainFactory,
    WorldEntry worldEntry,
    ILogger<RpgEnterWorldMessageHandler> logger
) : IMessageHandler<RpgEnterWorldMessage>
{
    public async ValueTask HandleAsync(
        RpgEnterWorldMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (
            !await grainFactory
                .GetAccountCharactersGrain(ctx.AccountId)
                .OwnsAsync(message.PlayerId)
                .ConfigureAwait(false)
        )
        {
            return;
        }

        logger.LogInformation(
            "Account {AccountId} enters the world as player {PlayerId}",
            ctx.AccountId,
            message.PlayerId
        );

        try
        {
            await worldEntry.EnterAsync(ctx.SessionKey, message.PlayerId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Half in the world: the account phase is already left. Closed like a failed SSO, so
            // the client starts again from a clean sign-in.
            logger.LogWarning(
                ex,
                "Failed to enter the world as player {PlayerId} for session {SessionKey}",
                message.PlayerId,
                ctx.SessionKey
            );

            await ctx.CloseSessionAsync().ConfigureAwait(false);
        }
    }
}
