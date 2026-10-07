using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Permissions;
using Vortex.Primitives.Permissions;

namespace Vortex.Permissions;

/// <summary>
/// Resolves admin-configured <see cref="SanctionPresetEntity"/> rows for the staff CFH tool's
/// ModBan/ModTradingLock dropdowns. Read-mostly, low call volume (one lookup per sanction applied) —
/// no caching needed, unlike <see cref="PermissionService"/>.
/// </summary>
public sealed class SanctionPresetService(IDbContextFactory<VortexDbContext> dbContextFactory)
    : ISanctionPresetService
{
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;

    public async Task<SanctionPresetSnapshot?> ResolveAsync(
        SanctionPresetKind kind,
        int presetIndex,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        SanctionPresetEntity? preset = await dbCtx
            .SanctionPresets.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Kind == kind && p.PresetIndex == presetIndex && p.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        return preset is null
            ? null
            : new SanctionPresetSnapshot(preset.Name, preset.DurationSeconds, preset.Message);
    }

    public async Task<SanctionPresetSnapshot?> ResolveByIdAsync(
        int presetId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        SanctionPresetEntity? preset = await dbCtx
            .SanctionPresets.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == presetId && p.DeletedAt == null, ct)
            .ConfigureAwait(false);

        return preset is null
            ? null
            : new SanctionPresetSnapshot(preset.Name, preset.DurationSeconds, preset.Message);
    }
}
