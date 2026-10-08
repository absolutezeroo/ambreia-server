using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.World;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;

namespace Vortex.World;

/// <summary>
/// Saves, publishes, rolls back and locks zones (spec §6). The only writer of <c>zones</c> and
/// <c>zone_versions</c>; a version is never deleted, so rolling back is publishing an older one.
/// </summary>
public sealed partial class ZoneEditorService(
    IDbContextFactory<VortexDbContext> db,
    ZoneValidator validator,
    IFurnitureDefinitionProvider definitions,
    IOptions<WorldConfig> config,
    ZoneRoomReloader reload,
    ILogger<ZoneEditorService> logger
) : IZoneEditorService
{
    private readonly WorldConfig _config = config.Value;

    public async Task<IReadOnlyList<ZoneSummary>> ListAsync(CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<ZoneEntity> zones = await ctx
            .Zones.AsNoTracking()
            .OrderBy(z => z.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        Dictionary<int, int> latest = await ctx
            .ZoneVersions.AsNoTracking()
            .GroupBy(v => v.ZoneEntityId)
            .Select(g => new { Zone = g.Key, Version = g.Max(v => v.Version) })
            .ToDictionaryAsync(x => x.Zone, x => x.Version, ct)
            .ConfigureAwait(false);
        Dictionary<int, string> names = await ctx
            .Rooms.AsNoTracking()
            .Where(r => zones.Select(z => z.RoomEntityId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name ?? string.Empty, ct)
            .ConfigureAwait(false);

        return
        [
            .. zones.Select(z => new ZoneSummary(
                z.Key,
                names.GetValueOrDefault(z.RoomEntityId, string.Empty),
                z.RoomEntityId,
                z.PublishedVersion,
                latest.GetValueOrDefault(z.Id),
                LockHolder(z)
            )),
        ];
    }

    public async Task<ZoneSummary> CreateAsync(
        string key,
        string name,
        int accountId,
        string? fromTemplate,
        CancellationToken ct
    )
    {
        if (!KeyShape().IsMatch(key))
        {
            throw new ArgumentException($"Zone key '{key}' is not 3-64 of [a-z0-9_].", nameof(key));
        }

        // A transient failure replays the whole attempt on a fresh context, as the content import
        // does: the retrying strategy refuses a transaction opened outside it.
        await using VortexDbContext owner = await db.CreateDbContextAsync(ct).ConfigureAwait(false);
        int roomId = await owner
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(() => CreateAttemptAsync(key, name, accountId, fromTemplate, ct))
            .ConfigureAwait(false);

        logger.LogInformation("Zone {Key} created (room {RoomId})", key, roomId);

        return new ZoneSummary(key, name, roomId, null, fromTemplate is null ? 0 : 1, null);
    }

    /// <summary>The room, the zone and the template's copy in one transaction: a failure part-way
    /// leaves nothing, where it used to leave a room no zone pointed at.</summary>
    private async Task<int> CreateAttemptAsync(
        string key,
        string name,
        int accountId,
        string? fromTemplate,
        CancellationToken ct
    )
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using IDbContextTransaction tx = await ctx
            .Database.BeginTransactionAsync(ct)
            .ConfigureAwait(false);

        if (await ctx.Zones.AnyAsync(z => z.Key == key, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Zone '{key}' already exists.");
        }

        // Resolved first: a missing template must not leave a half-created zone behind.
        ZoneDocument? template = fromTemplate is null
            ? null
            : await LoadPublishedAsync(ctx, fromTemplate, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException(
                    $"Template '{fromTemplate}' does not exist or is not published."
                );

        // A zone's room needs an owner and a model to exist. Neither is a choice: the world owner
        // is seeded by InitialCreate, the model comes with the reference data. The model is never
        // read for a zone (the map comes from the document).
        int? worldId = await ctx
            .Players.AsNoTracking()
            .Where(p => p.Name == "__world__")
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        int? modelId = await ctx
            .RoomModels.AsNoTracking()
            .Where(m => m.Name == "model_a")
            .OrderBy(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (worldId is null || modelId is null)
        {
            throw new InvalidOperationException(
                "The world's reference data is missing (player __world__, room model model_a): "
                    + "run scripts/import-reference-data.ps1 on this database."
            );
        }

        // The same minimal insert as scripts/sql/dev_zone.sql: every other column has a default.
        // The description marks the row as this zone's.
        string marker = $"zone:{key}";

        await ctx
            .Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO rooms (name, description, player_id, model_id)
                VALUES ({name}, {marker}, {worldId.Value}, {modelId.Value})
                """,
                ct
            )
            .ConfigureAwait(false);

        // Read back in the same transaction: no other row can carry this marker before it commits.
        int roomId = await ctx
            .Rooms.AsNoTracking()
            .Where(r => r.Description == marker)
            .OrderByDescending(r => r.Id)
            .Select(r => r.Id)
            .FirstAsync(ct)
            .ConfigureAwait(false);

        ZoneEntity zone = new() { Key = key, RoomEntityId = roomId };

        ctx.Zones.Add(zone);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        if (template is not null)
        {
            await CopyTemplateAsync(ctx, zone, template, fromTemplate!, name, accountId, ct)
                .ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);

        return roomId;
    }

    public async Task<string?> GetDocumentAsync(string key, int? version, CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity? zone = await FindAsync(ctx, key, ct).ConfigureAwait(false);
        int? wanted = version ?? zone?.PublishedVersion;

        if (zone is null || wanted is null)
        {
            return null;
        }

        return await ctx
            .ZoneVersions.AsNoTracking()
            .Where(v => v.ZoneEntityId == zone.Id && v.Version == wanted)
            .Select(v => v.Document)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetLatestDocumentAsync(string key, CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity? zone = await FindAsync(ctx, key, ct).ConfigureAwait(false);

        if (zone is null)
        {
            return null;
        }

        return await ctx
            .ZoneVersions.AsNoTracking()
            .Where(v => v.ZoneEntityId == zone.Id)
            .OrderByDescending(v => v.Version)
            .Select(v => v.Document)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ZoneVersionInfo>> GetVersionsAsync(
        string key,
        CancellationToken ct
    )
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity? zone = await FindAsync(ctx, key, ct).ConfigureAwait(false);

        if (zone is null)
        {
            return [];
        }

        return await ctx
            .ZoneVersions.AsNoTracking()
            .Where(v => v.ZoneEntityId == zone.Id)
            .OrderByDescending(v => v.Version)
            .Select(v => new ZoneVersionInfo(
                v.Version,
                v.AuthorAccountId,
                v.CreatedAt,
                v.Status == ZoneVersionStatus.Published,
                v.Comment
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ZoneFinding>> ValidateAsync(
        string key,
        string json,
        CancellationToken ct
    )
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        return (await ParseAndValidateAsync(ctx, key, json, ct).ConfigureAwait(false)).Findings;
    }

    public async Task<ZoneSaveResult> SaveAsync(
        string key,
        string json,
        int accountId,
        string comment,
        CancellationToken ct
    )
    {
        int version;

        await using (VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            ZoneEntity zone =
                await FindTrackedAsync(ctx, key, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Zone '{key}' does not exist.");

            if (LockHolder(zone) != accountId)
            {
                return Refused(
                    new ZoneFinding(
                        ZoneSeverity.Error,
                        "Locked",
                        null,
                        "Take the zone's edit lock before saving."
                    )
                );
            }

            (ZoneDocument? doc, IReadOnlyList<ZoneFinding> findings) = await ParseAndValidateAsync(
                    ctx,
                    key,
                    json,
                    ct
                )
                .ConfigureAwait(false);

            if (doc is null || findings.Any(f => f.Severity == ZoneSeverity.Error))
            {
                return new ZoneSaveResult(false, null, false, findings);
            }

            version =
                await ctx
                    .ZoneVersions.Where(v => v.ZoneEntityId == zone.Id)
                    .Select(v => (int?)v.Version)
                    .MaxAsync(ct)
                    .ConfigureAwait(false)
                ?? 0;
            version++;

            ctx.ZoneVersions.Add(
                new ZoneVersionEntity
                {
                    ZoneEntityId = zone.Id,
                    Version = version,
                    SchemaVersion = doc.SchemaVersion,
                    Document = ZoneJson.Serialize(doc),
                    AuthorAccountId = accountId,
                    Status = ZoneVersionStatus.Draft,
                    Comment = comment.Length > 512 ? comment[..512] : comment,
                }
            );
            zone.LockedAt = DateTime.UtcNow;

            try
            {
                await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex)
            {
                // Another save took this version number first (a double submit): nothing of this
                // one was written, and the editor can simply save again.
                logger.LogWarning(
                    ex,
                    "Zone {Key} version {Version} was saved concurrently",
                    key,
                    version
                );

                return Refused(
                    new ZoneFinding(
                        ZoneSeverity.Error,
                        "Conflict",
                        null,
                        $"Version {version} was saved by another request; save again."
                    )
                );
            }

            if (!_config.PublishOnSave)
            {
                return new ZoneSaveResult(true, version, false, findings);
            }
        }

        return await PublishAsync(key, version, ct).ConfigureAwait(false);
    }

    public async Task<ZoneSaveResult> PublishAsync(string key, int version, CancellationToken ct)
    {
        RoomId roomId;
        IReadOnlyList<ZoneFinding> findings;

        await using (VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            ZoneEntity zone =
                await FindTrackedAsync(ctx, key, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Zone '{key}' does not exist.");
            ZoneVersionEntity? row = await ctx
                .ZoneVersions.FirstOrDefaultAsync(
                    v => v.ZoneEntityId == zone.Id && v.Version == version,
                    ct
                )
                .ConfigureAwait(false);

            if (row is null)
            {
                return Refused(
                    new ZoneFinding(
                        ZoneSeverity.Error,
                        "NoSuchVersion",
                        null,
                        $"Zone '{key}' has no version {version}."
                    )
                );
            }

            // Again: other zones may have changed since this version was saved (a passage's
            // destination removed), and a publication must not ship what no longer holds.
            (_, findings) = await ParseAndValidateAsync(ctx, key, row.Document, ct)
                .ConfigureAwait(false);

            if (findings.Any(f => f.Severity == ZoneSeverity.Error))
            {
                return new ZoneSaveResult(true, version, false, findings);
            }

            // One SaveChanges is one transaction: the pointer and the status move together. A
            // hand-opened transaction is refused by Pomelo's retrying execution strategy.
            zone.PublishedVersion = version;
            row.Status = ZoneVersionStatus.Published;

            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

            roomId = new RoomId(zone.RoomEntityId);
        }

        logger.LogInformation("Zone {Key} version {Version} published", key, version);

        try
        {
            await reload.ReloadAsync(roomId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The publication is committed; a room that missed the reload picks the version up on
            // its next activation.
            logger.LogWarning(
                ex,
                "Zone {Key} published but room {RoomId} did not reload",
                key,
                roomId
            );
        }

        return new ZoneSaveResult(true, version, true, findings);
    }

    public async Task<bool> TryLockAsync(
        string key,
        int accountId,
        bool force,
        CancellationToken ct
    )
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity zone =
            await FindTrackedAsync(ctx, key, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Zone '{key}' does not exist.");
        int? holder = LockHolder(zone);

        if (holder is not null && holder != accountId && !force)
        {
            return false;
        }

        if (holder is not null && holder != accountId)
        {
            logger.LogWarning(
                "Zone {Key} lock forced by account {Account} (held by {Holder})",
                key,
                accountId,
                holder
            );
        }

        zone.LockedByAccountId = accountId;
        zone.LockedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task UnlockAsync(string key, int accountId, CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity? zone = await FindTrackedAsync(ctx, key, ct).ConfigureAwait(false);

        if (zone is null || zone.LockedByAccountId != accountId)
        {
            return;
        }

        zone.LockedByAccountId = null;
        zone.LockedAt = null;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public IReadOnlyList<FurniturePaletteEntry> GetPalette() =>
        [
            .. definitions
                .All()
                .Where(d => d.ProductType is ProductType.Floor or ProductType.Wall)
                .OrderBy(d => d.Id)
                .Select(d => new FurniturePaletteEntry(
                    d.Id,
                    d.Name,
                    d.ProductType == ProductType.Wall,
                    d.Width,
                    d.Length,
                    d.TotalStates,
                    d.CanWalk,
                    d.CanSit,
                    d.StackHeight.Value
                )),
        ];

    private async Task<(
        ZoneDocument? Doc,
        IReadOnlyList<ZoneFinding> Findings
    )> ParseAndValidateAsync(VortexDbContext ctx, string key, string json, CancellationToken ct)
    {
        ZoneDocument doc;

        try
        {
            doc = ZoneJson.Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return (null, [new ZoneFinding(ZoneSeverity.Error, "Json", null, ex.Message)]);
        }

        HashSet<string> known =
        [
            .. await ctx
                .Zones.AsNoTracking()
                .Select(z => z.Key)
                .ToListAsync(ct)
                .ConfigureAwait(false),
        ];
        List<ZoneFinding> findings = [.. validator.Validate(doc, known)];

        if (!string.Equals(doc.Settings.Id, key, StringComparison.Ordinal))
        {
            findings.Add(
                new ZoneFinding(
                    ZoneSeverity.Error,
                    "KeyMismatch",
                    null,
                    $"The document is zone '{doc.Settings.Id}', not '{key}'."
                )
            );
        }

        return (doc, findings);
    }

    private static async Task<ZoneDocument?> LoadPublishedAsync(
        VortexDbContext ctx,
        string key,
        CancellationToken ct
    )
    {
        ZoneEntity? source = await FindAsync(ctx, key, ct).ConfigureAwait(false);

        if (source?.PublishedVersion is not int published)
        {
            return null;
        }

        string? json = await ctx
            .ZoneVersions.AsNoTracking()
            .Where(v => v.ZoneEntityId == source.Id && v.Version == published)
            .Select(v => v.Document)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return json is null ? null : ZoneJson.Deserialize(json);
    }

    private static async Task CopyTemplateAsync(
        VortexDbContext ctx,
        ZoneEntity zone,
        ZoneDocument doc,
        string template,
        string name,
        int accountId,
        CancellationToken ct
    )
    {
        ctx.ZoneVersions.Add(
            new ZoneVersionEntity
            {
                ZoneEntityId = zone.Id,
                Version = 1,
                SchemaVersion = doc.SchemaVersion,
                Document = ZoneJson.Serialize(
                    doc with
                    {
                        Settings = doc.Settings with
                        {
                            Id = zone.Key,
                            Name = name,
                            Template = false,
                        },
                    }
                ),
                AuthorAccountId = accountId,
                Status = ZoneVersionStatus.Draft,
                Comment = $"from template {template}",
            }
        );

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private int? LockHolder(ZoneEntity zone) =>
        zone.LockedByAccountId is int holder
        && zone.LockedAt is DateTime at
        && at.AddMinutes(_config.LockMinutes) > DateTime.UtcNow
            ? holder
            : null;

    private static Task<ZoneEntity?> FindAsync(
        VortexDbContext ctx,
        string key,
        CancellationToken ct
    ) => ctx.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Key == key, ct);

    private static Task<ZoneEntity?> FindTrackedAsync(
        VortexDbContext ctx,
        string key,
        CancellationToken ct
    ) => ctx.Zones.FirstOrDefaultAsync(z => z.Key == key, ct);

    private static ZoneSaveResult Refused(ZoneFinding finding) =>
        new(false, null, false, [finding]);

    [GeneratedRegex("^[a-z0-9_]{3,64}$")]
    private static partial Regex KeyShape();
}
