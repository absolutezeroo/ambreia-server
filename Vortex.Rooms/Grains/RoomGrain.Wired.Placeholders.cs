using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Rooms.Wired;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task<ImmutableArray<KeyValuePair<string, string>>> GetGlobalPlaceholdersAsync(
        CancellationToken ct
    ) => WiredSystem.GetGlobalPlaceholdersAsync(ct);

    public async Task<string?> ResolveSharedPlaceholderAsync(
        int roomId,
        string name,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        return await WiredGlobalPlaceholderIndex.ResolveAsync(dbCtx, roomId, name, ct);
    }
}
