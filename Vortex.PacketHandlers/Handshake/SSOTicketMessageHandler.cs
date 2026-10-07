using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

namespace Vortex.PacketHandlers.Handshake;

/// <summary>
/// The core sign-in: ticket → session → AuthenticationOK, UserRights, then a forward into the world's
/// start zone. Nothing else is sent: the hotel-era login burst (wallet, effects, navigator, perks…)
/// left with the systems that answered it.
/// </summary>
public class SSOTicketMessageHandler(
    IAuthenticationService authService,
    ISessionGateway sessionGateway,
    IGrainFactory grainFactory,
    IPermissionService permissionService,
    ILogger<SSOTicketMessageHandler> logger
) : IMessageHandler<SSOTicketMessage>
{
    /// <summary>Server config key naming the room every login is forwarded into; 0 or absent = none.</summary>
    public const string START_ROOM_KEY = "world.start_room_id";

    private readonly IAuthenticationService _authService = authService;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IPermissionService _permissionService = permissionService;
    private readonly ILogger<SSOTicketMessageHandler> _logger = logger;

    public async ValueTask HandleAsync(
        SSOTicketMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        try
        {
            int playerId = await _authService
                .GetPlayerIdFromTicketAsync(message.SSO, ctx.RemoteIpAddress, ct)
                .ConfigureAwait(false);

            if (playerId <= 0)
            {
                await ctx.CloseSessionAsync().ConfigureAwait(false);

                return;
            }

            await _sessionGateway
                .AddSessionToPlayerAsync(ctx.SessionKey, playerId, ct)
                .ConfigureAwait(false);

            Task<PermissionSet> permissionsTask = _permissionService.ResolveForPlayerAsync(
                playerId,
                ct
            );
            Task<int> startRoomTask = _grainFactory
                .GetServerConfigGrain()
                .GetIntAsync(START_ROOM_KEY, 0);

            PermissionSet permissions = await permissionsTask.ConfigureAwait(false);
            int startRoomId = await startRoomTask.ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new AuthenticationOKMessage
                    {
                        AccountId = playerId,
                        SuggestedLoginActions = [],
                        IdentityId = playerId,
                    },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(
                    new UserRightsMessage
                    {
                        MembershipLevel = MembershipLevelType.None,
                        SecurityLevel = SecurityLevelPolicy.Resolve(permissions),
                        IsAmbassador = false,
                    },
                    ct
                )
                .ConfigureAwait(false);

            if (startRoomId <= 0)
            {
                // ponytail: no existence check — a forward to a deleted room fails at room entry,
                // which is where the client already handles a missing room.
                _logger.LogWarning(
                    "No start zone configured ({Key}); player {PlayerId} stays on the hotel view",
                    START_ROOM_KEY,
                    playerId
                );

                return;
            }

            await ctx.SendComposerAsync(
                    new RoomForwardMessageComposer { RoomId = RoomId.Parse(startRoomId) },
                    ct
                )
                .ConfigureAwait(false);
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
