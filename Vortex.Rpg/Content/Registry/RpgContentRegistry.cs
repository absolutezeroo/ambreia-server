using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Registry;

/// <summary>
/// Loads the content tables, validates them like an import, and swaps the served set whole (spec
/// Contenu §10). Invalid stored content throws and the previous set stays.
/// </summary>
public sealed class RpgContentRegistry(
    IDbContextFactory<VortexDbContext> dbFactory,
    IEnumerable<IContentKind> kinds,
    IFurnitureDefinitionProvider furniture,
    IEventPublisher events,
    ILogger<RpgContentRegistry> logger
) : IRpgContent, IReferenceDataProvider
{
    private readonly ContentPackValidator _validator = new(kinds);
    private readonly SemaphoreSlim _reloading = new(1, 1);
    private ContentSet _set = ContentSet.Empty;

    // Stage 1: an item names a furniture definition, so the catalogue (stage 0) loads first.
    public int LoadStage => 1;

    public long Version => Volatile.Read(ref _set).Version;

    public T? Get<T>(string id)
        where T : ContentDefinition =>
        Volatile.Read(ref _set).ById.TryGetValue(id, out ContentDefinition? found)
            ? found as T
            : null;

    public IReadOnlyList<T> All<T>()
        where T : ContentDefinition => Volatile.Read(ref _set).All<T>();

    /// <summary>One kind's live definitions as JSON, with the version of the very set they come from.</summary>
    public ContentDefinitions Definitions(string kind)
    {
        ContentSet set = Volatile.Read(ref _set);

        return new ContentDefinitions(set.Version, set.JsonOf(kind));
    }

    /// <summary>One reload at a time: two overlapping (a console reload during an import) would
    /// otherwise let the one that read first swap its older set in last.</summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        await _reloading.WaitAsync(ct);

        try
        {
            await ReloadOneAsync(ct);
        }
        finally
        {
            _reloading.Release();
        }
    }

    private async Task ReloadOneAsync(CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();

        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        List<ContentFile> files = await db
            .RpgContent.AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .Select(c => new ContentFile($"{c.Kind}/{c.ContentId}.json", c.Document))
            .ToListAsync(ct);
        Dictionary<string, string> texts = await db
            .RpgContentTexts.AsNoTracking()
            .Where(t => t.DeletedAt == null)
            .ToDictionaryAsync(t => t.Language, t => t.Body, ct);
        long version = await db.RpgContentPacks.AsNoTracking().MaxAsync(p => (long?)p.Id, ct) ?? 0;

        // Migrations were applied when their pack was imported: the stored rows are the result.
        ValidatedPack pack = _validator.Validate(
            new ContentPack(files, null, texts),
            id => furniture.TryGetDefinition(id) is not null
        );

        if (pack.HasErrors)
        {
            throw new ContentInvalidException([
                .. pack.Findings.Where(f => f.Severity == ContentSeverity.Error),
            ]);
        }

        FrozenDictionary<string, string> hashes = pack.Definitions.ToFrozenDictionary(
            d => d.Key,
            d => Hash(d.Value)
        );
        ContentSet previous = Volatile.Read(ref _set);
        Volatile.Write(
            ref _set,
            new ContentSet(version, pack.Definitions.ToFrozenDictionary(), hashes)
        );

        int added = hashes.Keys.Count(id => !previous.Hashes.ContainsKey(id));
        int changed = hashes.Count(h =>
            previous.Hashes.TryGetValue(h.Key, out string? old) && old != h.Value
        );
        int removed = previous.Hashes.Keys.Count(id => !hashes.ContainsKey(id));

        logger.LogInformation(
            "RPG content v{Version} loaded in {ElapsedMs}ms: {Counts}; {Warnings} warning(s).",
            version,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            string.Join(
                ", ",
                pack.Definitions.Values.GroupBy(d => d.Kind).Select(g => $"{g.Count()} {g.Key}")
            ),
            pack.Findings.Count
        );

        await events.PublishAsync(new ContentReloadedEvent(version, added, changed, removed), ct);
    }

    /// <summary>SHA-256 in hex of the canonical JSON: the same definition always hashes the same,
    /// whatever spacing or key order its file had.</summary>
    internal static string Hash(ContentDefinition definition) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(ContentJson.Serialize(definition)))
        );
}
