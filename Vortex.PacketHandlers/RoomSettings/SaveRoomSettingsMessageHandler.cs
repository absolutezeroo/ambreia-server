using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.RoomSettings;

namespace Vortex.PacketHandlers.RoomSettings;

public class SaveRoomSettingsMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<SaveRoomSettingsMessage>
{
    public async ValueTask HandleAsync(
        SaveRoomSettingsMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.RoomId <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message.RoomName))
        {
            return;
        }

        // The name and the description only — the password is a secret, never shown to anybody.
        // A refusal saves nothing: RoomSettingsSaveErrorEventMessageComposer is still an empty stub
        // on this server, so the bubble is what tells the owner why.
        string? name = await wordFilter
            .PassAsync(ctx, message.RoomName, WordFilterSource.Room, ct)
            .ConfigureAwait(false);
        string? description = name is null
            ? null
            : await wordFilter
                .PassAsync(ctx, message.RoomDescription, WordFilterSource.Room, ct)
                .ConfigureAwait(false);

        if (name is null || description is null)
        {
            return;
        }

        IRoomSettings roomGrain = grainFactory.GetRoomSettings(message.RoomId);

        await roomGrain
            .UpdateRoomSettingsAsync(
                ctx.PlayerId,
                new RoomSettingsUpdate
                {
                    Name = name,
                    Description = description,
                    DoorMode = (Vortex.Primitives.Rooms.Enums.RoomDoorModeType)message.DoorMode,
                    Password = message.Password,
                    MaxVisitors = message.MaxVisitors,
                    CategoryId = message.CategoryId,
                    TradeMode = message.TradeMode,
                    AllowPets = message.AllowPets,
                    AllowPetsEat = message.AllowFoodConsume,
                    AllowBlocking = message.AllowWalkThrough,
                    HideWalls = message.HideWalls,
                    WallThickness = message.WallThickness,
                    FloorThickness = message.FloorThickness,
                    WhoCanMute = message.WhoCanMute,
                    WhoCanKick = message.WhoCanKick,
                    WhoCanBan = message.WhoCanBan,
                    ChatFloodSensitivity = message.ChatFloodSensitivity,
                    LeaveOnDoorTile = message.LeaveOnDoorTileEnabled,
                    IdleSleepEnabled = message.IdleSleepEnabled,
                    IdleSleepTimeoutSeconds = message.IdleSleepTimeoutSeconds,
                    IdleAutokickEnabled = message.IdleAutokickEnabled,
                    IdleAutokickTimeoutSeconds = message.IdleAutokickTimeoutSeconds,
                    MuteAllPets = message.MuteAllPets,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
