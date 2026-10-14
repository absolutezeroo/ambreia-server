using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

namespace Vortex.Players.Grains;

/// <summary>
/// The player entering the world, once its socket is attached (spec F3a §5.4): AuthenticationOK,
/// UserRights and a forward to where it lands, then the character's state. The same path for a
/// player ticket and for a character chosen on the character screen. Nothing else is sent: the
/// hotel-era login burst left with the systems that answered it.
/// </summary>
internal sealed partial class PlayerPresenceGrain
{
    /// <summary>Server config key naming the room every login is forwarded into; 0 or absent = none.</summary>
    internal const string StartRoomKey = "world.start_room_id";

    private readonly IPermissionService _permissions;
    private readonly IZoneRoomSource _zoneSource;

    public async Task EnterWorldAsync(CancellationToken ct)
    {
        IComposer[] burst = await BuildWorldEntryAsync(ct);

        // Through this grain's own queue like everything else sent to a signed-in player, so the
        // burst cannot overtake or fall behind what the other grains send.
        await SendComposerAsync(burst);

        await JoinWorldChatAsync();

        if (burst[^1] is not RoomForwardMessageComposer)
        {
            // No zone to land in: no HUD on the hotel view.
            return;
        }

        // The HUD. Entering the zone pushes it too, but a resume within the grace period enters
        // no zone. A view only: its failure never undoes the sign-in.
        try
        {
            await _grainFactory.GetPlayerCharacterGrain(PlayerId).PushStateAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sign-in could not push the state of {PlayerId}", PlayerId);
        }

        // The quest log goes with the state (spec M2a §3.2), for the same reason: a resume enters
        // no zone. A view too, apart from the state's.
        try
        {
            await _grainFactory.GetQuestLogGrain(PlayerId).PushLogAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sign-in could not push the quest log of {PlayerId}", PlayerId);
        }
    }

    /// <summary>The sign-in burst, landing place decided: a pending editor test entry, else the
    /// room still held in grace, else the character's last zone if still published, else the start
    /// zone.</summary>
    internal async Task<IComposer[]> BuildWorldEntryAsync(CancellationToken ct)
    {
        int playerId = PlayerId.Value;

        Task<PermissionSet> permissionsTask = _permissions.ResolveForPlayerAsync(playerId, ct);
        Task<int> startRoomTask = _grainFactory.GetServerConfigGrain().GetIntAsync(StartRoomKey, 0);
        // The map editor's test mode: the zone under test, once, instead of the start zone.
        Task<RoomId?> testRoomTask = _grainFactory.GetZoneTestGrain().TakeAsync(PlayerId);

        // Where the character left the world (spec Personnage §9); null for an avatar without one.
        // A better place to land, never a reason not to: a failure falls through to the start zone.
        Task<RoomId?> lastRoomTask = OrNullAsync(
            () => _grainFactory.GetPlayerCharacterGrain(PlayerId).GetLastRoomAsync(),
            "last zone",
            playerId
        );

        // All awaited, so a failure of any is seen rather than dropped.
        await Task.WhenAll(permissionsTask, startRoomTask, testRoomTask, lastRoomTask);

        PermissionSet permissions = await permissionsTask;

        // A test entry places the avatar on the cell asked for: resuming the avatar left in grace
        // (perhaps in that very zone) would keep it where it stood. Its room is left on entry.
        if (await testRoomTask is not null)
        {
            _resumableRoom = -1;
        }

        int startRoomId =
            await testRoomTask is { } testRoom ? testRoom.Value
            // The room the avatar is still standing in, back within the grace period (§10).
            : _resumableRoom > 0 ? _resumableRoom.Value
            : await PublishedAsync(await lastRoomTask, ct) is { } lastRoom ? lastRoom.Value
            : await startRoomTask;

        List<IComposer> burst =
        [
            new AuthenticationOKMessage { AccountId = playerId, IdentityId = playerId },
            new UserRightsMessage { SecurityLevel = SecurityLevelPolicy.Resolve(permissions) },
        ];

        if (startRoomId <= 0)
        {
            // ponytail: no existence check — a forward to a deleted room fails at room entry,
            // which is where the client already handles a missing room.
            _logger.LogWarning(
                "No start zone configured ({Key}); player {PlayerId} stays on the hotel view",
                StartRoomKey,
                playerId
            );

            return [.. burst];
        }

        burst.Add(new RoomForwardMessageComposer { RoomId = RoomId.Parse(startRoomId) });

        return [.. burst];
    }

    private PlayerId PlayerId => new((int)this.GetPrimaryKeyLong());

    /// <summary>The last zone, if it is still a published zone: a zone taken down sends the player
    /// to the start rather than into a dead room.</summary>
    private Task<RoomId?> PublishedAsync(RoomId? room, CancellationToken ct) =>
        room is { } r
            ? OrNullAsync(
                async () => await _zoneSource.GetPublishedAsync(r, ct) is not null ? r : null,
                "published zone",
                r.Value
            )
            : Task.FromResult<RoomId?>(null);

    /// <summary>An optional landing place: its failure is logged and reads as "none".</summary>
    private async Task<RoomId?> OrNullAsync(Func<Task<RoomId?>> lookup, string what, int id)
    {
        try
        {
            return await lookup();
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
