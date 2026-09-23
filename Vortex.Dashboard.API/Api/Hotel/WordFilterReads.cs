using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orleans;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Moderation;
using Vortex.Observability.WordFilter;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;

namespace Vortex.Dashboard.API.Api.Hotel;

/// <summary>
/// The hotel-wide word list, the journal of what it caught, and a dry run of the live filter.
/// </summary>
internal sealed class WordFilterReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls,
    IWordFilter wordFilter,
    IGrainFactory grainFactory
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;
    private readonly IWordFilter _wordFilter = wordFilter;
    private readonly IGrainFactory _grainFactory = grainFactory;

    public Task<WordFilterWordList> WordsAsync(CancellationToken ct) =>
        QueryAsync(
            async db => new WordFilterWordList(
                await db
                    .WordFilterWords.AsNoTracking()
                    .OrderBy(w => w.Word)
                    .Select(w => new WordFilterWordRow(
                        w.Id,
                        w.Word,
                        w.Action,
                        w.Match,
                        w.Replacement,
                        w.CreatedBy,
                        w.CreatedAt
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false)
            ),
            ct
        );

    /// <summary>
    /// The journal, newest first. Filters: <c>playerId</c>, <c>source</c> (the enum's number).
    /// </summary>
    public Task<WordFilterHitPage> HitsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync(
            async db =>
            {
                int limit = QueryValues.Limit(query["limit"], 50, 200);
                int page = QueryValues.Page(query["page"]);

                IQueryable<WordFilterHitEntity> hits = db.WordFilterHits.AsNoTracking();

                if (int.TryParse(query["playerId"], out int playerId) && playerId > 0)
                {
                    hits = hits.Where(h => h.PlayerEntityId == playerId);
                }

                if (
                    int.TryParse(query["source"], out int source)
                    && Enum.IsDefined((WordFilterSource)source)
                )
                {
                    hits = hits.Where(h => h.Source == (WordFilterSource)source);
                }

                int total = await hits.CountAsync(ct).ConfigureAwait(false);

                List<WordFilterHitEntity> rows = await hits.OrderByDescending(h => h.OccurredAt)
                    .ThenByDescending(h => h.Id)
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                HashSet<int> playerIds = [.. rows.Select(h => h.PlayerEntityId)];
                HashSet<int> wordIds = [.. rows.Select(h => h.WordId)];

                Dictionary<int, (string Name, string Figure)> players = await db
                    .Players.AsNoTracking()
                    .Where(p => playerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => (p.Name, p.Figure), ct)
                    .ConfigureAwait(false);

                // Through soft deletes: a word taken off the list since still names what it caught.
                Dictionary<int, string> words = await db
                    .WordFilterWords.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(w => wordIds.Contains(w.Id))
                    .ToDictionaryAsync(w => w.Id, w => w.Word, ct)
                    .ConfigureAwait(false);

                List<WordFilterHitRow> items = rows.Select(h =>
                    {
                        bool known = players.TryGetValue(
                            h.PlayerEntityId,
                            out (string Name, string Figure) player
                        );

                        return new WordFilterHitRow(
                            h.Id,
                            h.PlayerEntityId,
                            known ? player.Name : null,
                            known ? _assetUrls.AvatarImage(player.Figure) : null,
                            h.Source,
                            h.WordId,
                            words.GetValueOrDefault(h.WordId),
                            h.Action,
                            h.OriginalText,
                            h.OccurredAt
                        );
                    })
                    .ToList();

                return new WordFilterHitPage(page, limit, total, items);
            },
            ct
        );

    /// <summary>
    /// What the filter would do with <paramref name="text"/>, journaling nothing. A read, not an
    /// operation: it changes nothing, so it takes no reason and leaves no audit row.
    /// </summary>
    /// <remarks>
    /// With no draft it asks the live filter. With one, it compiles a throwaway set — the saved
    /// words, the draft in place of the word it edits — because trying a word before saving it is
    /// the box's whole point, and the live set does not have it yet.
    /// </remarks>
    public async Task<WordFilterTestResult> TestAsync(
        string? text,
        WordFilterDraft? draft,
        CancellationToken ct
    )
    {
        text ??= string.Empty;

        if (draft is null || string.IsNullOrWhiteSpace(draft.Word))
        {
            WordFilterResult live = _wordFilter.Test(text);

            return new WordFilterTestResult(live.Text, live.Refused, live.Changed);
        }

        List<WordFilterEntry> entries = await QueryAsync(
                db =>
                    db.WordFilterWords.AsNoTracking()
                        .Where(w => w.Id != draft.Id)
                        .Select(w => new WordFilterEntry(
                            w.Id,
                            w.Word,
                            w.Action,
                            w.Match,
                            w.Replacement
                        ))
                        .ToListAsync(ct),
                ct
            )
            .ConfigureAwait(false);

        entries.Add(
            new WordFilterEntry(
                draft.Id ?? 0,
                draft.Word,
                draft.Action,
                draft.Match,
                string.IsNullOrWhiteSpace(draft.Replacement) ? null : draft.Replacement.Trim()
            )
        );

        string replacement =
            await _grainFactory
                .GetServerConfigGrain()
                .GetValueAsync(WordFilterConfig.ReplacementKey)
                .ConfigureAwait(false)
            ?? WordFilterConfig.ReplacementDefault;

        try
        {
            WordFilterVerdict verdict = WordFilterSet
                .Build(entries, replacement, DraftMatchTimeout)
                .Evaluate(text);

            return new WordFilterTestResult(verdict.Text, verdict.Refused, verdict.Changed);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            // The live filter fails open on these; the dry run says so the same way.
            return new WordFilterTestResult(text, false, false);
        }
    }

    private static readonly TimeSpan DraftMatchTimeout = TimeSpan.FromMilliseconds(50);
}
