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
    public async Task<ZoneDocument?> GetPublishedAsync(RoomId roomId, CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        string? json = await (
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
