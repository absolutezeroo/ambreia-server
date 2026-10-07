using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public int ToIdx(int x, int y) => MapModule.ToIdx(x, y);

    public void ComputeTile(int x, int y) => MapModule.ComputeTile(x, y);

    public void ComputeTile(int id) => MapModule.ComputeTile(id);

    public Task ClickTileAsync(ActionContext ctx, int x, int y, CancellationToken ct) =>
        MapModule.ClickTileAsync(ctx, x, y, ct);

    public Task<RoomTileSnapshot> GetTileSnapshotAsync(int x, int y, CancellationToken ct) =>
        MapModule.GetTileSnapshotAsync(x, y, ct);

    public Task<RoomTileSnapshot> GetTileSnapshotAsync(int id, CancellationToken ct) =>
        MapModule.GetTileSnapshotAsync(id, ct);

    public Task<RoomMapSnapshot> GetMapSnapshotAsync(CancellationToken ct) =>
        Task.FromResult(MapModule.GetMapSnapshot(ct));

    public Task<ImmutableArray<(int X, int Y)>> GetOccupiedTilesAsync(CancellationToken ct) =>
        Task.FromResult(MapModule.GetOccupiedTiles());

    private Task FlushDirtyTilesAsync(CancellationToken ct)
    {
        if (_state.DirtyHeightTileIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        HashSet<int> dirtyHeightTileIds = _state.DirtyHeightTileIds;
        _state.DirtyHeightTileIds = [];

        // The packet counts its tiles in one byte: a batch over 255 would wrap and desync the
        // client, whatever the config says.
        int perPacket = Math.Clamp(_roomConfig.MaxTileHeightsPerFlush, 1, byte.MaxValue);

        List<(int X, int Y, short Height)> heights = new List<(int X, int Y, short Height)>(
            Math.Min(dirtyHeightTileIds.Count, perPacket)
        );

        foreach (int x in dirtyHeightTileIds)
        {
            heights.Add((MapModule.GetX(x), MapModule.GetY(x), _state.TileEncodedHeights[x]));

            if (heights.Count == heights.Capacity)
            {
                SendComposerToRoomAsync(
                        new HeightMapUpdateMessageComposer { TileHeights = [.. heights] }
                    )
                    .LogAndForget(
                        _logger,
                        "Failed to publish height-map update for room {RoomId}",
                        _state.RoomId
                    );

                heights.Clear();
            }
        }

        if (heights.Count > 0)
        {
            SendComposerToRoomAsync(
                    new HeightMapUpdateMessageComposer { TileHeights = [.. heights] }
                )
                .LogAndForget(
                    _logger,
                    "Failed to publish height-map update for room {RoomId}",
                    _state.RoomId
                );
        }

        return Task.CompletedTask;
    }
}
