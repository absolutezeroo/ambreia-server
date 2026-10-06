using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Rooms;

internal sealed partial class RoomService
{
    public async Task<(RoomId RoomId, string Name)> CreateRoomAsync(
        string name,
        string description,
        string modelName,
        int categoryId,
        int maxPlayers,
        RoomTradeModeType tradeType,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // The limit was already being calculated -- in CanCreateRoomMessageHandler, which answers
        // the navigator's "may I?" screen and serves it to the client in CanCreateRoomMessageComposer
        // as RoomLimit. The protocol therefore announced a limit the server never enforced, and
        // CreateFlat is its own packet: a client that simply sends it never asks the question. With
        // no quota and no per-message rate limit, ten packets a second is thirty-six thousand rooms
        // an hour, one row each, for as long as somebody cares to keep sending.
        int maxRooms = await _grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(RoomsConfig.MaxRoomsPerPlayerKey, RoomsConfig.MaxRoomsPerPlayerDefault)
            .ConfigureAwait(false);

        int ownedRooms = await dbCtx
            .Rooms.CountAsync(r => r.PlayerEntityId == playerId.Value, ct)
            .ConfigureAwait(false);

        if (ownedRooms >= maxRooms)
        {
            throw new VortexException(VortexErrorCodeEnum.RoomLimitReached);
        }

        RoomModelEntity model =
            await dbCtx
                .RoomModels.FirstOrDefaultAsync(x => x.Name == modelName && x.DeletedAt == null, ct)
                .ConfigureAwait(false)
            ?? await dbCtx
                .RoomModels.FirstOrDefaultAsync(x => x.DeletedAt == null, ct)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No room models available.");

        PlayerEntity player =
            await dbCtx
                .Players.FirstOrDefaultAsync(x => x.Id == playerId.Value && x.DeletedAt == null, ct)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Player {playerId} not found.");

        // categoryId comes off the wire. A positive id that names no category used to reach the
        // insert and fail there on the foreign key -- an unhandled exception on a forgeable packet.
        if (
            categoryId > 0
            && !await dbCtx
                .NavigatorFlatCategories.AnyAsync(c => c.Id == categoryId, ct)
                .ConfigureAwait(false)
        )
        {
            throw new VortexException(VortexErrorCodeEnum.NavigatorCategoryNotFound);
        }

        // So does maxPlayers, and it is not merely a number: the room's own door reads
        // "PlayersMax > 0 && avatars >= PlayersMax", so zero or a negative turns the population
        // limit off entirely rather than setting it low. Clamped into the range the client's own
        // dialog can express.
        int cappedMaxPlayers = Math.Clamp(maxPlayers, 1, RoomsConfig.MaxPlayersCeiling);

        // Bounded, not just trimmed: the column declares no length, so what the wire sends is what
        // gets stored -- up to a whole packet body.
        string trimmedName = RoomsConfig.Clamp(name, RoomsConfig.MaxNameLength);
        string clampedDescription = RoomsConfig.Clamp(
            description,
            RoomsConfig.MaxDescriptionLength
        );

        RoomEntity room = new RoomEntity
        {
            Name = trimmedName,
            Description = clampedDescription,
            PlayerEntityId = player.Id,
            PlayerEntity = player,
            RoomModelEntityId = model.Id,
            RoomModelEntity = model,
            NavigatorCategoryEntityId = categoryId > 0 ? categoryId : null,
            DoorMode = RoomDoorModeType.Open,
            UsersNow = 0,
            PlayersMax = cappedMaxPlayers,
            TradeType = tradeType,
            // Decoration ids stay null until the owner applies one -- the grain reports the
            // client's default surface ("0") for an unset value.
            WallHeight = -1,
            HideWalls = false,
            ThicknessWall = RoomThicknessType.Normal,
            ThicknessFloor = RoomThicknessType.Normal,
            AllowBlocking = false,
            AllowPets = true,
            AllowPetsEat = false,
            MuteType = ModSettingType.Owner,
            KickType = ModSettingType.Owner,
            BanType = ModSettingType.Owner,
            ChatModeType = ChatModeType.FreeFlow,
            ChatBubbleType = ChatBubbleWidthType.Normal,
            ChatSpeedType = ChatScrollSpeedType.Normal,
            ChatFloodType = ChatFloodSensitivityType.Minimal,
            ChatDistance = 50,
            Score = 0,
        };

        dbCtx.Rooms.Add(room);
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Room created: RoomId={RoomId} Name={Name} Owner={PlayerId}",
            room.Id,
            trimmedName,
            playerId
        );

        await _events
            .PublishAsync(
                new RoomCreatedEvent(
                    playerId,
                    room.Id,
                    trimmedName,
                    room.Description,
                    model.Name,
                    room.NavigatorCategoryEntityId ?? 0
                ),
                ct
            )
            .ConfigureAwait(false);

        return (room.Id, trimmedName);
    }
}
