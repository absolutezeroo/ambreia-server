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
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
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
    IZoneRoomSource zoneSource,
    ILogger<SSOTicketMessageHandler> logger
) : IMessageHandler<SSOTicketMessage>
{
    /// <summary>Server config key naming the room every login is forwarded into; 0 or absent = none.</summary>
    public const string START_ROOM_KEY = "world.start_room_id";

    private readonly IAuthenticationService _authService = authService;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IPermissionService _permissionService = permissionService;
    private readonly IZoneRoomSource _zoneSource = zoneSource;
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
            // The map editor's test mode: the zone under test, once, instead of the start zone.
            Task<RoomId?> testRoomTask = _grainFactory
                .GetZoneTestGrain()
                .TakeAsync(new PlayerId(playerId));

            // Where the character left the world (spec Personnage §9); null for an avatar without one.
            // Both are a better place to land, never a reason not to: a failure falls through to
            // the start zone (spec Personnage §9: without a character, sign-in is as before).
            Task<RoomId?> lastRoomTask = OrNullAsync(
                () =>
                    _grainFactory
                        .GetPlayerCharacterGrain(new PlayerId(playerId))
                        .GetLastRoomAsync(),
                "last zone",
                playerId
            );

            // The room the avatar is still standing in, back within the grace period (§10).
            Task<RoomId?> graceRoomTask = OrNullAsync(
                () => _grainFactory.GetPlayerPresenceGrain(playerId).GetResumableRoomAsync(),
                "grace room",
                playerId
            );

            // All awaited, so a failure of any is seen rather than dropped.
            await Task.WhenAll(
                    permissionsTask,
                    startRoomTask,
                    testRoomTask,
                    lastRoomTask,
                    graceRoomTask
                )
                .ConfigureAwait(false);

            PermissionSet permissions = await permissionsTask.ConfigureAwait(false);
            int startRoomId =
                await testRoomTask.ConfigureAwait(false) is { } testRoom ? testRoom.Value
                : await graceRoomTask.ConfigureAwait(false) is { } graceRoom ? graceRoom.Value
                : await PublishedAsync(await lastRoomTask.ConfigureAwait(false), ct)
                    .ConfigureAwait(false)
                    is { } lastRoom
                    ? lastRoom.Value
                : await startRoomTask.ConfigureAwait(false);

            AuthenticationOKMessage authenticated = new()
            {
                AccountId = playerId,
                SuggestedLoginActions = [],
                IdentityId = playerId,
            };
            UserRightsMessage rights = new()
            {
                MembershipLevel = MembershipLevelType.None,
                SecurityLevel = SecurityLevelPolicy.Resolve(permissions),
                IsAmbassador = false,
            };

            // Through the presence queue like everything else sent to a signed-in player, so the
            // burst cannot overtake or fall behind what the grains send.
            IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(playerId);

            if (startRoomId <= 0)
            {
                // ponytail: no existence check — a forward to a deleted room fails at room entry,
                // which is where the client already handles a missing room.
                _logger.LogWarning(
                    "No start zone configured ({Key}); player {PlayerId} stays on the hotel view",
                    START_ROOM_KEY,
                    playerId
                );

                await presence.SendComposerAsync(authenticated, rights).ConfigureAwait(false);

                return;
            }

            await presence
                .SendComposerAsync(
                    authenticated,
                    rights,
                    new RoomForwardMessageComposer { RoomId = RoomId.Parse(startRoomId) }
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

    /// <summary>The last zone, if it is still a published zone: a zone taken down sends the player
    /// to the start rather than into a dead room.</summary>
    private Task<RoomId?> PublishedAsync(RoomId? room, CancellationToken ct) =>
        room is { } r
            ? OrNullAsync(
                async () =>
                    await _zoneSource.GetPublishedAsync(r, ct).ConfigureAwait(false) is not null
                        ? r
                        : null,
                "published zone",
                r.Value
            )
            : Task.FromResult<RoomId?>(null);

    /// <summary>An optional landing place: its failure is logged and reads as "none".</summary>
    private async Task<RoomId?> OrNullAsync(Func<Task<RoomId?>> lookup, string what, int id)
    {
        try
        {
            return await lookup().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Sign-in could not read the {What} for {Id}; skipping it.",
                what,
                id
            );

            return null;
        }
    }
}
