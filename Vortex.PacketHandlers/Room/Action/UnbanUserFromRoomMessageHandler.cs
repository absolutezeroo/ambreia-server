using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;

namespace Vortex.PacketHandlers.Room.Action;

public class UnbanUserFromRoomMessageHandler(
    IGrainFactory grainFactory,
    IPermissionService permissionService,
    IEventPublisher events
) : IMessageHandler<UnbanUserFromRoomMessage>
{
    public async ValueTask HandleAsync(
        UnbanUserFromRoomMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.UserId <= 0)
        {
            return;
        }

        // The banned-users tab lives in the room settings window, which is normally opened from
        // the navigator - the player is rarely standing in the room being edited. Same shape as
        // BanUserWithDurationMessageHandler, and RoomModerationSystem rejects an ActionContext
        // whose RoomId is not the grain's own, so the context has to be re-pointed too.
        RoomId actorRoomId = message.RoomId > 0 ? new RoomId(message.RoomId) : ctx.RoomId;
        if (actorRoomId <= 0)
        {
            return;
        }

        ActionContext actorCtx = ctx.AsActionContext() with { RoomId = actorRoomId };
        // Room-scoped: the owner / rights-holders / guild members are authorized by the room's
        // own mod settings inside RoomGrain. All that is enforced here is that nobody sanctions
        // higher-ranked staff.
        if (
            !await RoomModerationGuard
                .CanActOnTargetAsync(
                    permissionService,
                    events,
                    ctx,
                    actorRoomId,
                    message.UserId,
                    ModerationAction.Ban,
                    ct
                )
                .ConfigureAwait(false)
        )
        {
            return;
        }

        IRoomModeration roomGrain = grainFactory.GetRoomModeration(actorRoomId);
        bool applied = await roomGrain
            .UnbanUserAsync(actorCtx, message.UserId, ct)
            .ConfigureAwait(false);

        if (applied)
        {
            // Back to the operator, not to the room: the ban list is a window they have open, and
            // RoomSettingsCtrl drops the row only on this message. Same session the request came
            // from, exactly like the list itself in GetBannedUsersFromRoomMessageHandler.
            await ctx.SendComposerAsync(
                    new UserUnbannedFromRoomEventMessageComposer
                    {
                        RoomId = actorRoomId.Value,
                        PlayerId = message.UserId,
                    },
                    ct
                )
                .ConfigureAwait(false);

            return;
        }

        // The room's mod settings did not grant this actor authority here.
        await RoomModerationGuard
            .AuditDenialAsync(events, ctx, actorRoomId, message.UserId, ModerationAction.Ban, ct)
            .ConfigureAwait(false);
    }
}
