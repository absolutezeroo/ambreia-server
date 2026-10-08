using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Vortex.Database.Entities.World;

namespace Vortex.Database.Tests.Support;

/// <summary>Fails any save that writes a zone version, as a database lost in the middle of a
/// zone's creation would: what was written before it must not stay behind.</summary>
internal sealed class FailingZoneVersionInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default
    ) =>
        eventData
            .Context!.ChangeTracker.Entries<ZoneVersionEntity>()
            .Any(e => e.State == EntityState.Added)
            ? throw new InvalidOperationException("database lost")
            : ValueTask.FromResult(result);
}
