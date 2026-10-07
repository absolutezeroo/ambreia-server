using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Migrations;
using Vortex.Rpg.Content.Registry;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Import;

/// <summary>
/// The import (spec Contenu §6): validate the whole pack, diff it against the tables by hash, refuse
/// a removal players still hold without a replace rule, write everything in one transaction, then
/// reload the registry.
/// </summary>
public sealed class RpgContentService(
    IDbContextFactory<VortexDbContext> dbFactory,
    IEnumerable<IContentKind> kinds,
    IEnumerable<IContentReferences> references,
    IFurnitureDefinitionProvider furniture,
    RpgContentRegistry registry,
    ILogger<RpgContentService> logger
) : IRpgContentService
{
    private readonly ContentPackValidator _validator = new(kinds);
    private readonly IContentReferences[] _references = [.. references];

    // ponytail: one import at a time in this one silo; a second host would need a database lock.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ContentImportResult> ImportAsync(
        ContentPack pack,
        int accountId,
        CancellationToken ct
    )
    {
        await _gate.WaitAsync(ct);

        try
        {
            return await ImportOneAsync(pack, accountId, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetTextsAsync(string language, CancellationToken ct)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        return await db
            .RpgContentTexts.AsNoTracking()
            .Where(t => t.Language == language && t.DeletedAt == null)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<ContentImportResult> ImportOneAsync(
        ContentPack pack,
        int accountId,
        CancellationToken ct
    )
    {
        long started = Stopwatch.GetTimestamp();
        ValidatedPack valid = _validator.Validate(
            pack,
            id => furniture.TryGetDefinition(id) is not null
        );

        if (valid.HasErrors)
        {
            return Refused(valid.Findings);
        }

        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        // Soft-deleted rows too: an id added back revives its row (content_id is unique).
        Dictionary<string, RpgContentEntity> rows = await db
            .RpgContent.IgnoreQueryFilters()
            .ToDictionaryAsync(c => c.ContentId, ct);
        Dictionary<string, RpgContentTextEntity> textRows = await db
            .RpgContentTexts.IgnoreQueryFilters()
            .ToDictionaryAsync(t => t.Language, ct);
        HashSet<(string, string?)> applied =
        [
            .. (
                await db
                    .RpgContentMigrations.Select(m => new { m.FromContentId, m.ToContentId })
                    .ToListAsync(ct)
            ).Select(m => (m.FromContentId, m.ToContentId)),
        ];

        Dictionary<string, string> hashes = valid.Definitions.ToDictionary(
            d => d.Key,
            d => RpgContentRegistry.Hash(d.Value)
        );
        string[] added =
        [
            .. hashes.Keys.Where(id => !rows.TryGetValue(id, out RpgContentEntity? r) || !Live(r)),
        ];
        string[] changed =
        [
            .. hashes
                .Where(h =>
                    rows.TryGetValue(h.Key, out RpgContentEntity? r) && Live(r) && r.Hash != h.Value
                )
                .Select(h => h.Key),
        ];
        RpgContentEntity[] removed =
        [
            .. rows.Values.Where(r => Live(r) && !hashes.ContainsKey(r.ContentId)),
        ];
        ContentMigrationRule[] rules =
        [
            .. valid.Rules.Where(r =>
                r.Action == ContentMigrationAction.Replace && !applied.Contains((r.From, r.To))
            ),
        ];
        Dictionary<string, string> texts = (
            pack.Texts ?? new Dictionary<string, string>()
        ).ToDictionary(t => t.Key, t => (t.Value ?? string.Empty).TrimStart(ContentParser.Bom));
        bool textsChanged =
            texts.Any(t =>
                !textRows.TryGetValue(t.Key, out RpgContentTextEntity? r)
                || !Live(r)
                || r.Body != t.Value
            ) || textRows.Values.Any(r => Live(r) && !texts.ContainsKey(r.Language));

        if (added.Length + changed.Length + removed.Length + rules.Length == 0 && !textsChanged)
        {
            return new ContentImportResult(true, registry.Version, 0, 0, 0, valid.Findings);
        }

        List<ContentFinding> held = [];

        foreach (RpgContentEntity row in removed)
        {
            bool replaced = valid.Rules.Any(r =>
                r.From == row.ContentId && r.Action == ContentMigrationAction.Replace
            );

            if (!replaced && await IsReferencedAsync(db, row.Kind, row.ContentId, ct))
            {
                held.Add(
                    new ContentFinding(
                        ContentSeverity.Error,
                        "ReferencedWithoutMigration",
                        null,
                        row.ContentId,
                        $"Players still hold '{row.ContentId}': keep it with \"retired\": true, or add a replace rule to migrations.json."
                    )
                );
            }
        }

        if (held.Count > 0)
        {
            return Refused([.. valid.Findings, .. held]);
        }

        RpgContentPackEntity written = new()
        {
            AccountId = accountId,
            Added = added.Length,
            Changed = changed.Length,
            Removed = removed.Length,
        };

        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);

            db.RpgContentPacks.Add(written);

            foreach (string id in added.Concat(changed))
            {
                ContentDefinition definition = valid.Definitions[id];

                if (!rows.TryGetValue(id, out RpgContentEntity? row))
                {
                    row = new RpgContentEntity
                    {
                        ContentId = id,
                        Kind = definition.Kind,
                        Document = string.Empty,
                        Hash = string.Empty,
                        PackId = 0,
                    };
                    db.RpgContent.Add(row);
                }

                row.Kind = definition.Kind;
                row.Document = ContentJson.Serialize(definition);
                row.Hash = hashes[id];
                row.DeletedAt = null;
                row.Pack = written;
            }

            foreach (RpgContentEntity row in removed)
            {
                row.DeletedAt = DateTime.UtcNow;
                row.Pack = written;
            }

            foreach (ContentMigrationRule rule in rules)
            {
                string kind = valid.Definitions[rule.To!].Kind;

                foreach (IContentReferences refs in _references.Where(r => r.Kind == kind))
                {
                    await refs.ReplaceAsync(db, rule.From, rule.To!, ct);
                }

                db.RpgContentMigrations.Add(
                    new RpgContentMigrationEntity
                    {
                        FromContentId = rule.From,
                        Action = rule.Action,
                        ToContentId = rule.To,
                        PackId = 0,
                        Pack = written,
                    }
                );
            }

            WriteTexts(db, textRows, texts);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        await registry.ReloadAsync(ct);

        logger.LogInformation(
            "Content pack v{Version} imported by account {AccountId} in {ElapsedMs}ms: {Added} added, {Changed} changed, {Removed} removed, {Rules} migration rule(s), {Warnings} warning(s).",
            written.Id,
            accountId,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            added.Length,
            changed.Length,
            removed.Length,
            rules.Length,
            valid.Findings.Count
        );

        return new ContentImportResult(
            true,
            written.Id,
            added.Length,
            changed.Length,
            removed.Length,
            valid.Findings
        );
    }

    private async Task<bool> IsReferencedAsync(
        VortexDbContext db,
        string kind,
        string contentId,
        CancellationToken ct
    )
    {
        foreach (IContentReferences refs in _references.Where(r => r.Kind == kind))
        {
            if (await refs.IsReferencedAsync(db, contentId, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteTexts(
        VortexDbContext db,
        Dictionary<string, RpgContentTextEntity> rows,
        Dictionary<string, string> texts
    )
    {
        foreach ((string language, string body) in texts)
        {
            if (!rows.TryGetValue(language, out RpgContentTextEntity? row))
            {
                db.RpgContentTexts.Add(
                    new RpgContentTextEntity { Language = language, Body = body }
                );
                continue;
            }

            row.Body = body;
            row.DeletedAt = null;
        }

        foreach (RpgContentTextEntity row in rows.Values.Where(r => !texts.ContainsKey(r.Language)))
        {
            row.DeletedAt ??= DateTime.UtcNow;
        }
    }

    private static bool Live(Database.Entities.VortexEntity row) => row.DeletedAt is null;

    private ContentImportResult Refused(IReadOnlyList<ContentFinding> findings) =>
        new(false, registry.Version, 0, 0, 0, findings);
}
