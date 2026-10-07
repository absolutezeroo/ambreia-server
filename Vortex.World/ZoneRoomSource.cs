using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;

namespace Vortex.World;

/// <summary>The published document of the zone a room runs (spec §3.1).</summary>
public sealed class ZoneRoomSource(
    IDbContextFactory<VortexDbContext> db,
    ILogger<ZoneRoomSource> logger
) : IZoneRoomSource
{
    /// <summary>
    /// The room of each published zone, once found. Nothing invalidates it because nothing has to:
    /// a zone's room is set when the zone is made, and a published zone stays published (rolling
    /// back publishes an older version). "Not published yet" is never remembered.
    /// </summary>
    private readonly ConcurrentDictionary<string, RoomId> _publishedRooms = new();

    public async Task<RoomId?> FindPublishedRoomAsync(string zoneKey, CancellationToken ct)
    {
        if (_publishedRooms.TryGetValue(zoneKey, out RoomId known))
        {
            return known;
        }

        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        int? roomId = await ctx
            .Zones.AsNoTracking()
            .Where(z => z.Key == zoneKey && z.PublishedVersion != null)
            .Select(z => (int?)z.RoomEntityId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (roomId is not { } id)
        {
            return null;
        }

        return _publishedRooms[zoneKey] = new RoomId(id);
    }

    public async Task<ZoneDocument?> GetPublishedAsync(RoomId roomId, CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        string? json;

        // A database created before the zone tables (an older check database, a restored backup)
        // must still open every room -- as an ordinary one -- instead of failing every entry.
        try
        {
            json = await (
                from z in ctx.Zones.AsNoTracking()
                join v in ctx.ZoneVersions.AsNoTracking()
                    on new { Zone = z.Id, Version = z.PublishedVersion ?? -1 } equals new
                    {
                        Zone = v.ZoneEntityId,
                        Version = v.Version,
                    }
                where z.RoomEntityId == roomId.Value
                select v.Document
            )
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            logger.LogError(
                ex,
                "Zone lookup failed for room {RoomId}; opening it as an ordinary room",
                roomId
            );

            return null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            return ZoneJson.Deserialize(json);
        }
        catch (JsonException ex)
        {
            // Validated when saved, so only a schema change can land here. The room still opens,
            // as an ordinary one, rather than not at all.
            logger.LogError(
                ex,
                "Published document of room {RoomId}'s zone does not parse",
                roomId
            );

            return null;
        }
    }
}
