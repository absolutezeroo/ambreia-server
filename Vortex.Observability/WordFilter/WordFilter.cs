using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Observability.Audit;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;

namespace Vortex.Observability.WordFilter;

/// <summary>One journaled hit, before the writer stamps it and turns it into a row.</summary>
public sealed record WordFilterHit(
    int PlayerId,
    WordFilterSource Source,
    int WordId,
    WordFilterAction Action,
    string OriginalText
);

/// <summary>
/// The hotel-wide word filter: one compiled set behind a volatile reference, swapped whole on
/// reload.
/// </summary>
/// <remarks>
/// <para>
/// Empty until the first <see cref="ReloadAsync"/>, which <see cref="WordFilterLoader"/> runs once the
/// host — and with it the silo the config grain needs — has started. Until then everything passes;
/// the window is the seconds between boot and the first accepted socket.
/// </para>
/// <para>
/// <c>ponytail:</c> a reload refreshes this silo only. The hotel runs one silo, shared in-process by
/// the grains, the web API and the dashboard; with several, the admin service's reload becomes a
/// broadcast (an Orleans stream every silo's filter subscribes to).
/// </para>
/// </remarks>
public sealed class WordFilter : IWordFilter, IDisposable
{
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(50);

    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    private readonly Func<
        CancellationToken,
        Task<(IReadOnlyList<WordFilterEntry> Words, string Replacement, string Refused)>
    > _source;
    private readonly Action<WordFilterHit> _journal;
    private readonly ILogger<WordFilter> _logger;
    private readonly TimeSpan _matchTimeout;

    private volatile WordFilterSet _set = WordFilterSet.Empty;
    private volatile string _refusedMessage = WordFilterConfig.RefusedMessageDefault;

    public WordFilter(
        IDbContextFactory<VortexDbContext> dbCtxFactory,
        IGrainFactory grainFactory,
        AuditChannel channel,
        IVortexContextAccessor contextAccessor,
        ILogger<WordFilter> logger
    )
        : this(
            ct => LoadAsync(dbCtxFactory, grainFactory, ct),
            hit => Enqueue(channel, contextAccessor, logger, hit),
            logger
        ) { }

    /// <summary>The seam the tests build through: where the list comes from, where hits go.</summary>
    internal WordFilter(
        Func<
            CancellationToken,
            Task<(IReadOnlyList<WordFilterEntry> Words, string Replacement, string Refused)>
        > source,
        Action<WordFilterHit> journal,
        ILogger<WordFilter> logger,
        TimeSpan? matchTimeout = null
    )
    {
        _source = source;
        _journal = journal;
        _logger = logger;
        _matchTimeout = matchTimeout ?? DefaultMatchTimeout;
    }

    public string RefusedMessage => _refusedMessage;

    public WordFilterResult Apply(string text, WordFilterSource source, int playerId)
    {
        WordFilterVerdict verdict = Evaluate(text);

        if (verdict.FirstWordId == 0)
        {
            return new WordFilterResult(verdict.Text, false, false);
        }

        // A key with "bobba" spliced into it — a name, a search tag — is not one anyone chose: any
        // hit refuses it.
        bool refused = verdict.Refused || WordFilterSourceRules.IsStrict(source);

        _journal(
            new WordFilterHit(
                playerId,
                source,
                verdict.FirstWordId,
                refused ? WordFilterAction.Refuse : WordFilterAction.Replace,
                text.Length > WordFilterConfig.JournalTextMaxLength
                    ? text[..WordFilterConfig.JournalTextMaxLength]
                    : text
            )
        );

        return refused
            ? new WordFilterResult(text, true, false)
            : new WordFilterResult(verdict.Text, false, verdict.Changed);
    }

    public WordFilterResult Test(string text)
    {
        WordFilterVerdict verdict = Evaluate(text);

        return new WordFilterResult(verdict.Text, verdict.Refused, verdict.Changed);
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        // Read and swap under one gate. Two edits saved at once each end on a reload, and without
        // it the earlier read can land last and install a list missing the later edit — silently,
        // until somebody edits again.
        await _reloadGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            (IReadOnlyList<WordFilterEntry> words, string replacement, string refused) =
                await _source(ct).ConfigureAwait(false);

            _set = WordFilterSet.Build(words, replacement, _matchTimeout);
            _refusedMessage = refused;
        }
        finally
        {
            _reloadGate.Release();
        }

        _logger.LogInformation(
            VortexEventIds.WordFilterLoaded,
            "Word filter loaded: {Count} word(s)",
            _set.Count
        );
    }

    public void Dispose() => _reloadGate.Dispose();

    private WordFilterVerdict Evaluate(string text)
    {
        try
        {
            return _set.Evaluate(text);
        }
        catch (RegexMatchTimeoutException ex)
        {
            // Fail open: a line nobody can filter in time is let through rather than thrown into the
            // chat path. Logged so a pattern that times out on real input gets looked at.
            _logger.LogWarning(
                VortexEventIds.WordFilterTimedOut,
                ex,
                "Word filter timed out on a {Length}-character text; let through",
                text.Length
            );

            return new WordFilterVerdict(text, false, false, 0, default);
        }
        catch (ArgumentException ex)
        {
            // Same reasoning for a text the normaliser cannot take: the one known case (a
            // noncharacter) is handled there, and anything like it must not cost a player their line.
            _logger.LogWarning(
                VortexEventIds.WordFilterTimedOut,
                ex,
                "Word filter could not read a {Length}-character text; let through",
                text.Length
            );

            return new WordFilterVerdict(text, false, false, 0, default);
        }
    }

    private static async Task<(IReadOnlyList<WordFilterEntry>, string, string)> LoadAsync(
        IDbContextFactory<VortexDbContext> dbCtxFactory,
        IGrainFactory grainFactory,
        CancellationToken ct
    )
    {
        // GetManyAsync answers only the keys an operator has set; the rest take their defaults.
        ImmutableDictionary<string, string> config = await grainFactory
            .GetServerConfigGrain()
            .GetManyAsync(WordFilterConfig.Keys)
            .ConfigureAwait(false);

        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<WordFilterEntry> words = await db
            .WordFilterWords.AsNoTracking()
            .Select(w => new WordFilterEntry(w.Id, w.Word, w.Action, w.Match, w.Replacement))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (
            words,
            config.GetValueOrDefault(
                WordFilterConfig.ReplacementKey,
                WordFilterConfig.ReplacementDefault
            ),
            config.GetValueOrDefault(
                WordFilterConfig.RefusedMessageKey,
                WordFilterConfig.RefusedMessageDefault
            )
        );
    }

    private static void Enqueue(
        AuditChannel channel,
        IVortexContextAccessor contextAccessor,
        ILogger logger,
        WordFilterHit hit
    )
    {
        WordFilterHitRecord record = new(
            hit,
            DateTime.UtcNow,
            contextAccessor.Current?.CorrelationId.Value
        );

        if (!channel.TryWrite(record))
        {
            logger.LogWarning(
                VortexEventIds.WordFilterHitDropped,
                "Word filter hit dropped (channel saturated): player {PlayerId} word {WordId}",
                hit.PlayerId,
                hit.WordId
            );
        }
    }
}
