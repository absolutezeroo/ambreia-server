using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Vortex.Database.Entities.World;

namespace Vortex.Database.Tests.Support;

/// <summary>
/// A second save landing first: just before a new zone version is written, the same version number
/// is inserted underneath it, as a concurrent request that read the same maximum would.
/// </summary>
internal sealed class ConflictingVersionInterceptor : SaveChangesInterceptor
{
    private const string EmptyDocument = "{}";

    private bool _done;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default
    )
    {
        ZoneVersionEntity? pending = eventData
            .Context!.ChangeTracker.Entries<ZoneVersionEntity>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .FirstOrDefault();

        if (!_done && pending is not null)
        {
            _done = true;

            await eventData.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO zone_versions (zone_id, version, schema_version, document, author_account_id, status, comment, created_at, updated_at)
                VALUES ({pending.ZoneEntityId}, {pending.Version}, 1, {EmptyDocument}, 99, 0, 'concurrent', datetime('now'), datetime('now'))
                """,
                ct
            );
        }

        return result;
    }
}
