using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Rpg.Content;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>
/// Reads the pack the tables hold (content authoring §3): each live document's source at its path,
/// or, for a row imported before sources were kept, its normalised form at its derived path.
/// </summary>
public sealed class StoredPackReader(IDbContextFactory<VortexDbContext> dbFactory)
{
    public async Task<ContentExport> ReadAsync(CancellationToken ct)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        RpgContentEntity[] rows = await db
            .RpgContent.AsNoTracking()
            .OrderBy(c => c.ContentId)
            .ToArrayAsync(ct);
        RpgContentPackEntity? latest = await db
            .RpgContentPacks.AsNoTracking()
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        return new ContentExport(
            latest?.Id ?? 0,
            [
                .. rows.Select(r => new ContentFile(
                    r.SourcePath ?? ContentPaths.Derive(r.Kind, r.ContentId),
                    r.Source ?? r.Document
                )),
            ],
            latest?.Migrations,
            await db
                .RpgContentTexts.AsNoTracking()
                .ToDictionaryAsync(t => t.Language, t => t.Body, ct)
        );
    }
}
