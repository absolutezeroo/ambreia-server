using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities;
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
    private readonly HashSet<string> _public = [.. kinds.Where(k => k.Public).Select(k => k.Kind)];

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

    public ContentDefinitions? GetDefinitions(string kind) =>
        _public.Contains(kind) ? registry.Definitions(kind) : null;

    public IReadOnlyList<MonsterSummary> GetMonsters() =>
        [
            .. registry
                .All<MonsterDefinition>()
                .Select(m => new MonsterSummary(
                    m.Id,
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(m.Rank.ToString()),
                    m.Behavior,
                    m.Aggressive,
                    m.Figure,
                    m.Retired
                )),
        ];

    public IReadOnlyList<ClassSummary> GetClasses() =>
        [
            .. registry
                .All<ClassDefinition>()
                .Where(c => !c.Retired)
                .Select(c => new ClassSummary(
                    c.Id,
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(c.Role.ToString()),
                    c.Range,
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(c.PrimaryStat.ToString()),
                    c.Specializations,
                    c.Starter
                )),
        ];

    public IReadOnlyList<string> GetItemIds() =>
        [.. registry.All<ItemDefinition>().Where(i => !i.Retired).Select(i => i.Id)];

    public IReadOnlyList<string> GetQuestIds() =>
        [.. registry.All<QuestDefinition>().Where(q => !q.Retired).Select(q => q.Id)];

    public IReadOnlyList<string> GetDialogueIds() =>
        [.. registry.All<DialogueDefinition>().Where(d => !d.Retired).Select(d => d.Id)];

    public async Task<string?> GetTextsAsync(string language, CancellationToken ct)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        return await db
            .RpgContentTexts.AsNoTracking()
            .Where(t => t.Language == language)
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
            return LogRefused(Refused(valid.Findings), accountId, started);
        }

        Dictionary<string, string> hashes = valid.Definitions.ToDictionary(
            d => d.Key,
            d => RpgContentRegistry.Hash(d.Value)
        );
        Dictionary<string, string> texts = (
            pack.Texts ?? new Dictionary<string, string>()
        ).ToDictionary(t => t.Key, t => (t.Value ?? string.Empty).TrimStart(ContentParser.Bom));

        // A transient failure replays the whole attempt on a fresh context: it reads the tables
        // again, so a commit that went through before its answer was lost now diffs as no change.
        await using VortexDbContext owner = await dbFactory.CreateDbContextAsync(ct);
        (ContentImportResult result, long stored, int rules) = await owner
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(() => AttemptAsync(valid, hashes, texts, pack.Migrations, accountId, ct));

        if (!result.Imported)
        {
            return LogRefused(result, accountId, started);
        }

        // Past the commit the data is written: a client hanging up must not leave the registry on
        // the old version, so neither the reload nor the log listen to its token.
        if (registry.Version != stored)
        {
            await registry.ReloadAsync(CancellationToken.None);
        }

        logger.LogInformation(
            "Content pack v{Version} imported by account {AccountId} in {ElapsedMs}ms: {Added} added, {Changed} changed, {Removed} removed, {Rules} migration rule(s), {Warnings} warning(s).",
            result.PackVersion,
            accountId,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            result.Added,
            result.Changed,
            result.Removed,
            rules,
            result.Findings.Count
        );

        return result;
    }

    /// <summary>One attempt, all inside one transaction: read, diff, check the references, write.
    /// Returns the result, the version the tables now hold, and how many rules it applied.</summary>
    private async Task<(ContentImportResult Result, long Stored, int Rules)> AttemptAsync(
        ValidatedPack valid,
        Dictionary<string, string> hashes,
        Dictionary<string, string> texts,
        string? migrations,
        int accountId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);

        // Soft-deleted rows too: an id added back revives its row (content_id is unique).
        Dictionary<string, RpgContentEntity> rows = await db
            .RpgContent.IgnoreQueryFilters()
            .ToDictionaryAsync(c => c.ContentId, ct);
        Dictionary<string, RpgContentTextEntity> textRows = await db
            .RpgContentTexts.IgnoreQueryFilters()
            .ToDictionaryAsync(t => t.Language, ct);
        long stored = await db.RpgContentPacks.MaxAsync(p => (long?)p.Id, ct) ?? 0;

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

        // A replace rule acts when its id leaves the pack, this import: every time it does (the
        // rewrite is idempotent), never while the id is still live.
        ContentMigrationRule[] rules =
        [
            .. valid.Rules.Where(r =>
                r.Action == ContentMigrationAction.Replace
                && removed.Any(row => row.ContentId == r.From)
            ),
        ];
        bool textsChanged =
            texts.Any(t =>
                !textRows.TryGetValue(t.Key, out RpgContentTextEntity? r)
                || !Live(r)
                || r.Body != t.Value
            ) || textRows.Values.Any(r => Live(r) && !texts.ContainsKey(r.Language));

        if (added.Length + changed.Length + removed.Length == 0 && !textsChanged)
        {
            // The same content laid out otherwise: the sources follow, the version does not move.
            if (WriteSources(rows, valid.Sources))
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            return (new ContentImportResult(true, stored, 0, 0, 0, valid.Findings), stored, 0);
        }

        List<ContentFinding> held = [];

        foreach (RpgContentEntity row in removed)
        {
            if (
                !rules.Any(r => r.From == row.ContentId)
                && await IsReferencedAsync(db, row.Kind, row.ContentId, ct)
            )
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
            return (Refused([.. valid.Findings, .. held]), stored, 0);
        }

        RpgContentPackEntity written = new()
        {
            AccountId = accountId,
            Added = added.Length,
            Changed = changed.Length,
            Removed = removed.Length,
            Migrations = migrations,
        };
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
                rows[id] = row;
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
        WriteSources(rows, valid.Sources);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (
            new ContentImportResult(
                true,
                written.Id,
                added.Length,
                changed.Length,
                removed.Length,
                valid.Findings
            ),
            written.Id,
            rules.Length
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

    /// <summary>Each document's file as sent, on its row (content authoring §3); true when one moved.
    /// <paramref name="rows"/> holds the rows this import added too.</summary>
    private static bool WriteSources(
        Dictionary<string, RpgContentEntity> rows,
        IReadOnlyDictionary<string, ContentFile> sources
    )
    {
        bool moved = false;

        foreach ((string id, ContentFile file) in sources)
        {
            if (
                !rows.TryGetValue(id, out RpgContentEntity? row)
                || row.SourcePath == file.Path && row.Source == file.Text
            )
            {
                continue;
            }

            row.SourcePath = file.Path;
            row.Source = file.Text;
            moved = true;
        }

        return moved;
    }

    private static bool Live(VortexEntity row) => row.DeletedAt is null;

    private ContentImportResult Refused(IReadOnlyList<ContentFinding> findings) =>
        new(false, registry.Version, 0, 0, 0, findings);

    private ContentImportResult LogRefused(ContentImportResult result, int accountId, long started)
    {
        logger.LogWarning(
            "Content pack from account {AccountId} refused in {ElapsedMs}ms: {Errors} error(s), first {First}.",
            accountId,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            result.Findings.Count(f => f.Severity == ContentSeverity.Error),
            result.Findings.FirstOrDefault(f => f.Severity == ContentSeverity.Error)?.Code
        );

        return result;
    }
}
