using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Object.Furniture.Wall;

namespace Vortex.Rooms.Grains.Zones.Compilation;

/// <summary>
/// Turns a published zone document into what a room runs on: a Habbo model and decor objects.
/// The decor has no furniture rows, so its object ids only need to be unique within the room.
/// </summary>
internal static class ZoneRoomLoader
{
    /// <summary>Base-33 tile heights of the Habbo model string; 'x' (a hole) is the next letter.</summary>
    private const string Heights = "0123456789abcdefghijklmnopqrstuvw";

    public static RoomModelSnapshot CompileModel(
        ZoneDocument zone,
        int roomId,
        IRoomModelProvider models
    )
    {
        ZoneFloor floor = zone.Floor;
        StringBuilder model = new();

        for (int y = 0; y < floor.Length; y++)
        {
            if (y > 0)
            {
                model.Append('\r');
            }

            for (int x = 0; x < floor.Width; x++)
            {
                ZoneTile tile = floor.Tiles[y * floor.Width + x];

                model.Append(tile.Hole ? 'x' : Heights[tile.Height]);
            }
        }

        // A negative id: a zone's model is compiled from its document and is never in room_models.
        RoomModelSnapshot compiled = models.CompileCustomModel(
            -roomId,
            zone.Settings.Name,
            model.ToString(),
            floor.Door.X,
            floor.Door.Y,
            (Rotation)floor.DoorDirection
        );

        RoomTileFlags[] flags = [.. compiled.BaseFlags];

        for (int i = 0; i < floor.Tiles.Count; i++)
        {
            if (floor.Tiles[i] is { Walkable: false, Hole: false })
            {
                flags[i] = RoomTileFlags.Closed;
            }
        }

        return compiled with
        {
            BaseFlags = flags,
        };
    }

    public static (
        IReadOnlyList<IRoomFloorItem> Floor,
        IReadOnlyList<IRoomWallItem> Wall
    ) BuildDecor(
        ZoneDocument zone,
        PlayerId owner,
        IFurnitureDefinitionProvider definitions,
        IStuffDataFactory stuff,
        ILogger logger
    )
    {
        List<IRoomFloorItem> floor = [];
        List<IRoomWallItem> wall = [];

        for (int i = 0; i < zone.Decor.Count; i++)
        {
            ZoneDecorObject o = zone.Decor[i];
            FurnitureDefinitionSnapshot? def = definitions.TryGetDefinition(o.Definition);

            if (def is not { ProductType: ProductType.Floor })
            {
                logger.LogWarning(
                    "Zone decor {Id} uses unknown floor furniture {Definition}; left out.",
                    o.Id,
                    o.Definition
                );

                continue;
            }

            RoomFloorItem item = new()
            {
                ObjectId = i + 1,
                OwnerId = owner,
                OwnerName = string.Empty,
                Definition = def,
            };

            item.SetPosition(o.X, o.Y);
            item.SetPositionZ(o.Z);
            item.SetRotation((Rotation)o.Rotation);
            SetState(item, o.State, stuff);
            floor.Add(item);
        }

        for (int j = 0; j < zone.WallDecor.Count; j++)
        {
            ZoneWallDecorObject o = zone.WallDecor[j];
            FurnitureDefinitionSnapshot? def = definitions.TryGetDefinition(o.Definition);

            if (def is not { ProductType: ProductType.Wall })
            {
                logger.LogWarning(
                    "Zone wall decor {Id} uses unknown wall furniture {Definition}; left out.",
                    o.Id,
                    o.Definition
                );

                continue;
            }

            RoomWallItem item = new()
            {
                ObjectId = zone.Decor.Count + j + 1,
                OwnerId = owner,
                OwnerName = string.Empty,
                Definition = def,
            };

            // The wall position reads ":w=X,Y l=OffsetX,Z l|r": South is the left wall.
            item.SetPosition(o.X, o.Y);
            item.SetPositionZ(o.OffsetY);
            item.SetRotation(o.Side == ZoneWallSide.Left ? Rotation.South : Rotation.East);
            item.SetWallOffset(o.OffsetX);
            SetState(item, o.State, stuff);
            wall.Add(item);
        }

        return (floor, wall);
    }

    /// <summary>
    /// The published state goes where a furniture logic reads it: the stuff section of the extra
    /// data, in the definition's stuff format. A bare "2" there was no section at all, and every
    /// decor showed its first state in game.
    /// </summary>
    private static void SetState(IRoomItem item, int state, IStuffDataFactory stuff)
    {
        item.SetExtraData(null);

        if (state == 0)
        {
            return;
        }

        IStuffData data = stuff.CreateStuffData(item.Definition.StuffDataType);

        data.SetState(state.ToString(CultureInfo.InvariantCulture));
        item.ExtraData.UpdateSection(
            ExtraDataSectionType.STUFF,
            JsonSerializer.SerializeToNode(data, data.GetType())
        );
    }
}
