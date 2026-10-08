using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;

namespace Vortex.WebApi.Services;

public sealed class WebApiPlayerService(IDbContextFactory<VortexDbContext> dbCtxFactory)
    : IWebApiPlayerService
{
    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;

    public async Task<int?> FirstPlayerIdAsync(int accountId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db
            .Players.AsNoTracking()
            .Where(p => p.PlayerAccountEntityId == accountId && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
