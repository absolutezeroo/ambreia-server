using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Object.Avatars.Npc;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// A zone's NPCs in its room (spec M2a §3.3): put on their cells once, when the room is made ready.
/// An NPC whose cell is not on the map is left out and said so: the room still opens.
/// </summary>
public sealed class RoomNpcModule(RoomGrain roomGrain)
{
    private readonly Dictionary<int, IRoomNpc> _byObjectId = [];
    private bool _spawned;

    public IReadOnlyCollection<IRoomNpc> Npcs => _byObjectId.Values;

    public IRoomNpc? NpcAt(int objectId) => _byObjectId.GetValueOrDefault(objectId);

    public void EnsureSpawned()
    {
        if (_spawned || roomGrain._state.Zone is not { } zone)
        {
            return;
        }

        _spawned = true;

        foreach (ZoneNpc npc in zone.Npcs)
        {
            if (!roomGrain.MapModule.InBounds(npc.Cell.X, npc.Cell.Y))
            {
                roomGrain._logger.LogWarning(
                    "NPC {NpcId} of room {RoomId} stands at ({X}, {Y}), outside the map: left out",
                    npc.Id,
                    roomGrain.RoomId.Value,
                    npc.Cell.X,
                    npc.Cell.Y
                );

                continue;
            }

            RoomNpcAvatar avatar = new()
            {
                ObjectId = new RoomObjectId(roomGrain.AvatarModule.NextObjectId()),
                NpcId = npc.Id,
                Dialogue = npc.Dialogue,
                NpcName = npc.Name ?? npc.Id,
                Look = string.IsNullOrWhiteSpace(npc.Look) ? RoomNpcAvatar.DefaultFigure : npc.Look,
            };

            roomGrain._state.AvatarsByObjectId[avatar.ObjectId] = avatar;
            int idx = roomGrain.MapModule.ToIdx(npc.Cell.X, npc.Cell.Y);
            roomGrain.MapModule.RollAvatar(
                avatar,
                idx,
                roomGrain.MapModule.GetTileHeightForAvatar(idx)
            );
            avatar.SetRotation((Rotation)npc.Rotation);
            _byObjectId[avatar.ObjectId.Value] = avatar;
        }
    }
}
