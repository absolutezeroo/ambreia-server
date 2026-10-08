using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.World;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;

namespace Vortex.World;

/// <summary>A document checked, saved as a new version, and published: the pointer moves and the
/// room reloads. Every step validates again, because other zones may have changed in between.</summary>
public sealed partial class ZoneEditorService
{
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
}
