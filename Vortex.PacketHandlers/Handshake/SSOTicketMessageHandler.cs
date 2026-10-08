using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Sessions;
using Vortex.Protocol.Messages.Incoming.Handshake;

namespace Vortex.PacketHandlers.Handshake;

/// <summary>
/// The core sign-in. A player ticket enters the world (<see cref="IWorldEntry"/>); an account ticket
/// opens the character screen, the account phase (spec F3a §4): the character list and the class
/// list, and nothing of the world until a character is chosen.
/// </summary>
public class SSOTicketMessageHandler(
    IAuthenticationService authService,
    ISessionGateway sessionGateway,
    IGrainFactory grainFactory,
    IWorldEntry worldEntry,
    ILogger<SSOTicketMessageHandler> logger
) : IMessageHandler<SSOTicketMessage>
{
    private readonly IAuthenticationService _authService = authService;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWorldEntry _worldEntry = worldEntry;
    private readonly ILogger<SSOTicketMessageHandler> _logger = logger;

    public async ValueTask HandleAsync(
        SSOTicketMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        try
        {
            TicketOwner owner = await _authService
                .ResolveTicketAsync(message.SSO, ctx.RemoteIpAddress, ct)
                .ConfigureAwait(false);

            if (owner.PlayerId > 0)
            {
                await _worldEntry
                    .EnterAsync(ctx.SessionKey, owner.PlayerId, ct)
                    .ConfigureAwait(false);

                return;
            }

            if (owner.AccountId > 0)
            {
                await _sessionGateway
                    .AddSessionToAccountAsync(ctx.SessionKey, owner.AccountId, ct)
                    .ConfigureAwait(false);
                await _grainFactory
                    .GetAccountCharactersGrain(owner.AccountId)
                    .OpenAsync()
                    .ConfigureAwait(false);

                return;
            }

            await ctx.CloseSessionAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CloseSessionSafelyAsync(ctx).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to complete SSO handshake for session {SessionKey}",
                ctx.SessionKey
            );

            await CloseSessionSafelyAsync(ctx).ConfigureAwait(false);
        }
    }

    private async Task CloseSessionSafelyAsync(MessageContext ctx)
    {
        try
        {
            await ctx.CloseSessionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Closing session {SessionKey} after a failed SSO", ctx.SessionKey);
        }
    }
}
