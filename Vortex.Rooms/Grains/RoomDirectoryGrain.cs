using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains;

[KeepAlive]
public class RoomDirectoryGrain(
    IOptions<RoomConfig> roomConfig,
    ILogger<IRoomDirectoryGrain> logger,
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : Grain, IRoomDirectoryGrain
{
    private readonly RoomConfig _roomConfig = roomConfig.Value;
    private readonly ILogger<IRoomDirectoryGrain> _logger = logger;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;

    private readonly Dictionary<RoomId, RoomActiveSnapshot> _activeRooms = [];
    private readonly Dictionary<RoomId, HashSet<PlayerId>> _roomPlayers = [];
    private readonly Dictionary<RoomId, int> _roomPopulations = [];

    /// <summary>Occupancy the database has not been told about yet, room to headcount. Only what
    /// changed since the last flush, so a quiet hotel writes nothing at all.</summary>
    private readonly Dictionary<RoomId, int> _unflushedPopulations = [];

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        this.RegisterGrainTimer<object?>(
            static async (self, tickCt) =>
                await ((RoomDirectoryGrain)self!).CheckRoomsAsync(tickCt),
            this,
            TimeSpan.FromMilliseconds(_roomConfig.RoomCheckMs),
            TimeSpan.FromMilliseconds(_roomConfig.RoomCheckMs)
        );

        this.RegisterGrainTimer<object?>(
            static async (self, tickCt) =>
                await ((RoomDirectoryGrain)self!).FlushRoomActivityAsync(tickCt),
            this,
            TimeSpan.FromMilliseconds(_roomConfig.RoomPopulationFlushMs),
            TimeSpan.FromMilliseconds(_roomConfig.RoomPopulationFlushMs)
        );

        // There is one activation of this grain for the whole cluster and it holds every room's
        // occupancy in memory, so at this point no room has anyone in it. Whatever the last silo
        // left in the column is a ghost — a hotel that crashed with a hundred in the lobby would
        // otherwise advertise that lobby as full until someone walked back into it.
        await ClearStaleOccupancyAsync(ct).ConfigureAwait(true);
    }

    public Task UpsertActiveRoomAsync(RoomInfoSnapshot snapshot)
    {
        if (snapshot is not null)
        {
            _activeRooms[snapshot.RoomId] = new RoomActiveSnapshot
            {
                RoomId = snapshot.RoomId,
                Name = snapshot.Name,
                Description = snapshot.Description,
                OwnerId = snapshot.OwnerId,
                OwnerName = snapshot.OwnerName,
                Population = 0,
                LastUpdatedUtc = DateTime.UtcNow,
            };
        }

        return Task.CompletedTask;
    }

    public Task RemoveActiveRoomAsync(RoomId roomId)
    {
        // The occupancy maps have to go with it. This grain is [KeepAlive], so anything left behind
        // here is kept for the lifetime of the silo — one stale entry per room ever opened.
        _activeRooms.Remove(roomId);
        _roomPlayers.Remove(roomId);
        _roomPopulations.Remove(roomId);

        // Queued after the removal, not before: dropping the in-memory entry is exactly the moment
        // the room's last persisted headcount became a lie.
        _unflushedPopulations[roomId] = 0;

        return Task.CompletedTask;
    }

    public async Task AddPlayerToRoomAsync(PlayerId playerId, RoomId roomId, CancellationToken ct)
    {
        if (!_roomPlayers.TryGetValue(roomId, out HashSet<PlayerId>? playerIds))
        {
            playerIds = [];
            _roomPlayers[roomId] = playerIds;
        }

        playerIds.Add(playerId);

        await UpdatePopulationAsync(roomId);
    }

    public async Task RemovePlayerFromRoomAsync(
        PlayerId playerId,
        RoomId roomId,
        CancellationToken ct
    )
    {
        if (!_roomPlayers.TryGetValue(roomId, out HashSet<PlayerId>? players))
        {
            return;
        }

        if (!players.Remove(playerId))
        {
            return;
        }

        await UpdatePopulationAsync(roomId);
    }

    public Task<ImmutableArray<RoomSummarySnapshot>> GetActiveRoomsAsync() =>
        Task.FromResult(
            _activeRooms
                .Values.Select(x =>
                {
                    int population = _roomPopulations.TryGetValue(x.RoomId, out int pop) ? pop : 0;

                    return new RoomSummarySnapshot
                    {
                        RoomId = x.RoomId,
                        Name = x.Name,
                        Description = x.Description,
                        OwnerId = x.OwnerId,
                        OwnerName = x.OwnerName,
                        Population = population,
                        LastUpdatedUtc = x.LastUpdatedUtc,
                    };
                })
                .ToImmutableArray()
        );

    public Task<int> GetRoomPopulationAsync(RoomId roomId) =>
        Task.FromResult(_roomPopulations.TryGetValue(roomId, out int pop) ? pop : 0);

    private Task UpdatePopulationAsync(RoomId roomId)
    {
        int population = _roomPlayers.TryGetValue(roomId, out HashSet<PlayerId>? players)
            ? players.Count
            : 0;

        _roomPopulations[roomId] = population;
        _unflushedPopulations[roomId] = population;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes the occupancy the hotel has actually been living with back to <c>rooms.users_now</c>.
    /// </summary>
    /// <remarks>
    /// The column was set to 0 when a room was created and never written again, so every navigator
    /// list — all of which order and page on it in SQL — ranked the whole hotel by rating alone, and
    /// the category visitor counts, which are a <c>SUM</c> of it, read 0 forever.
    /// <para>
    /// The pending set is drained before the first await so this grain's own state is consistent
    /// for the whole write, and the writes are grouped by headcount: distinct headcounts in one
    /// window are few, where distinct rooms are not. ponytail: one statement per distinct headcount.
    /// A single <c>UPDATE … CASE</c> is the upgrade if a busy hotel makes the round trips show.
    /// </para>
    /// </remarks>
    internal async Task FlushRoomActivityAsync(CancellationToken ct)
    {
        Dictionary<RoomId, int> pending = new(_unflushedPopulations);

        _unflushedPopulations.Clear();

        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            await using VortexDbContext dbCtx = await _dbContextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            DateTime now = DateTime.UtcNow;

            foreach (
                IGrouping<int, KeyValuePair<RoomId, int>> group in pending.GroupBy(x => x.Value)
            )
            {
                int[] roomIds = [.. group.Select(x => x.Key.Value)];
                int population = group.Key;

                // last_active only moves for a room that had someone in it. Bumping it as a room
                // empties would make "last active" mean "last abandoned".
                if (population > 0)
                {
                    await dbCtx
                        .Rooms.Where(r => roomIds.Contains(r.Id))
                        .ExecuteUpdateAsync(
                            s =>
                                s.SetProperty(r => r.UsersNow, population)
                                    .SetProperty(r => r.LastActive, now),
                            ct
                        )
                        .ConfigureAwait(true);
                }
                else
                {
                    await dbCtx
                        .Rooms.Where(r => roomIds.Contains(r.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsersNow, 0), ct)
                        .ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex)
        {
            // Logged, not rethrown: an unreachable database must not take the room directory down
            // with it, and the next tick recomputes from memory anyway. What it cannot recompute is
            // this window's writes, so they go back on the queue.
            foreach ((RoomId roomId, int population) in pending)
            {
                _unflushedPopulations.TryAdd(roomId, population);
            }

            _logger.LogError(ex, "Failed to flush room occupancy for {Count} rooms", pending.Count);
        }
    }

    /// <summary>Drops occupancy left behind by a previous silo. Failure is logged rather than
    /// thrown: a stale headcount is a wrong room list, but refusing to activate this grain is no
    /// hotel at all.</summary>
    internal async Task ClearStaleOccupancyAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbContextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            int cleared = await dbCtx
                .Rooms.Where(r => r.UsersNow != 0)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsersNow, 0), ct)
                .ConfigureAwait(true);

            if (cleared > 0)
            {
                _logger.LogInformation(
                    "Cleared stale occupancy on {Count} rooms left by a previous run",
                    cleared
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear stale room occupancy at startup");
        }
    }

    /// <summary>
    /// Tells every active room whether to stay up. Fire-and-forget by construction: both calls are
    /// <c>[OneWay]</c>, so this turn hands off N messages and ends rather than waiting on N rooms.
    /// </summary>
    /// <remarks>
    /// This grain has one activation for the whole cluster and every room entry and exit in the
    /// hotel passes through it, so anything this method waits for, the whole hotel waits for.
    /// Awaiting the fan-out froze it for the length of the slowest room -- and a room that was
    /// itself mid-call to this directory could not answer until this turn ended, which is two
    /// non-reentrant grains waiting on each other until Orleans times one out.
    /// <para>
    /// ponytail: still one message per active room per sweep. That is a real cost at ten thousand
    /// rooms and it is no longer a stall -- sharding this grain by roomId bucket is the next step if
    /// the send itself starts to show.
    /// </para>
    /// </remarks>
    private Task CheckRoomsAsync(CancellationToken ct)
    {
        foreach (RoomActiveSnapshot room in _activeRooms.Values.ToArray())
        {
            int population = _roomPopulations.TryGetValue(room.RoomId, out int pop) ? pop : 0;
            IRoomCore roomGrain = _grainFactory.GetRoomCore(room.RoomId);

            // Discarded rather than logged: a one-way call's task is already completed when it
            // returns and can never carry a fault, so there is nothing to observe.
            if (population > 0)
            {
                _ = roomGrain.DelayRoomDeactivationAsync();
            }
            else
            {
                _ = roomGrain.DeactivateRoomAsync();
            }
        }

        return Task.CompletedTask;
    }
}
