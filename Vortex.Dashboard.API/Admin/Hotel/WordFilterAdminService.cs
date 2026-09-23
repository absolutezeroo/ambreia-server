using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Moderation;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Dashboard.API.Admin.Hotel;

/// <summary>
/// Staff's edits to the hotel-wide word list: the rows, then the reload.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than beside the filter in <c>Vortex.Observability</c> for the same reason the feed's
/// takedown is here: the list is authoring data only the dashboard edits, and the filter only reads.
/// The live obligation (§14 of the dashboard architecture) is the <see cref="IWordFilter.ReloadAsync"/>
/// at the end of every write, and it is not caught — a reload that fails is an edit the hotel does
/// not see, and the operation runner has to record it as a failure.
/// </para>
/// <para>
/// Uniqueness is on <see cref="WordFilterWordEntity.Key"/>, and the index covers soft-deleted rows
/// too. So a word added again revives its dead row, and an edit onto a dead row's key frees that
/// key rather than colliding with it.
/// </para>
/// </remarks>
public sealed class WordFilterAdminService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IWordFilter wordFilter
) : IWordFilterAdminService
{
    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async Task<WordFilterAdminOutcome> AddAsync(
        string word,
        WordFilterAction action,
        WordFilterMatch match,
        string? replacement,
        string actor,
        CancellationToken ct
    )
    {
        if (!TryNormalize(word, replacement, out string trimmed, out string key, out string? kept))
        {
            return WordFilterAdminOutcome.Invalid;
        }

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WordFilterWordEntity? existing = await db
            .WordFilterWords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Key == key, ct)
            .ConfigureAwait(false);

        if (existing is { DeletedAt: null })
        {
            return WordFilterAdminOutcome.Duplicate;
        }

        if (existing is null)
        {
            db.WordFilterWords.Add(
                new WordFilterWordEntity
                {
                    Word = trimmed,
                    Key = key,
                    Action = action,
                    Match = match,
                    Replacement = kept,
                    CreatedBy = actor,
                }
            );
        }
        else
        {
            existing.DeletedAt = null;
            existing.Word = trimmed;
            existing.Action = action;
            existing.Match = match;
            existing.Replacement = kept;
            existing.CreatedBy = actor;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _wordFilter.ReloadAsync(ct).ConfigureAwait(false);

        return WordFilterAdminOutcome.Done;
    }

    public async Task<WordFilterAdminOutcome> UpdateAsync(
        int id,
        string word,
        WordFilterAction action,
        WordFilterMatch match,
        string? replacement,
        CancellationToken ct
    )
    {
        if (!TryNormalize(word, replacement, out string trimmed, out string key, out string? kept))
        {
            return WordFilterAdminOutcome.Invalid;
        }

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WordFilterWordEntity? row = await db
            .WordFilterWords.FirstOrDefaultAsync(w => w.Id == id, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return WordFilterAdminOutcome.NotFound;
        }

        WordFilterWordEntity? holder = await db
            .WordFilterWords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Key == key && w.Id != id, ct)
            .ConfigureAwait(false);

        if (holder is { DeletedAt: null })
        {
            return WordFilterAdminOutcome.Duplicate;
        }

        if (holder is not null)
        {
            // A dead word holding the key: it gives it up and keeps its text for the journal. `~`
            // never survives the normaliser, so no live key can ever take this shape.
            holder.Key = $"~{holder.Id}";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        row.Word = trimmed;
        row.Key = key;
        row.Action = action;
        row.Match = match;
        row.Replacement = kept;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _wordFilter.ReloadAsync(ct).ConfigureAwait(false);

        return WordFilterAdminOutcome.Done;
    }

    public async Task<WordFilterAdminOutcome> RemoveAsync(int id, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        // The soft-delete filter means an already-removed word is not found, so a retried request
        // says so rather than reporting a second success.
        WordFilterWordEntity? row = await db
            .WordFilterWords.FirstOrDefaultAsync(w => w.Id == id, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return WordFilterAdminOutcome.NotFound;
        }

        row.DeletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _wordFilter.ReloadAsync(ct).ConfigureAwait(false);

        return WordFilterAdminOutcome.Done;
    }

    /// <summary>
    /// The shared validation: a trimmed word of 1-64 characters with at least one letter or digit
    /// the matcher can use, and a replacement that fits its column. A blank replacement means the
    /// default and is stored as null.
    /// </summary>
    private static bool TryNormalize(
        string word,
        string? replacement,
        out string trimmed,
        out string key,
        out string? kept
    )
    {
        trimmed = (word ?? string.Empty).Trim();
        key = WordFilterNormalizer.Key(trimmed);
        kept = string.IsNullOrWhiteSpace(replacement) ? null : replacement.Trim();

        return trimmed.Length is > 0 and <= WordFilterConfig.WordMaxLength
            && key.Length > 0
            && (kept is null || kept.Length <= WordFilterConfig.WordMaxLength);
    }
}
