using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Observability.WordFilter;

/// <summary>One listed word as the matcher needs it — the row minus its bookkeeping.</summary>
public sealed record WordFilterEntry(
    int Id,
    string Word,
    WordFilterAction Action,
    WordFilterMatch Match,
    string? Replacement
);

/// <summary>
/// What the set decided about one text. <see cref="FirstWordId"/> is the word that decided it — the
/// refusing word when refused, the earliest-listed replacing word otherwise — and 0 when nothing
/// matched.
/// </summary>
public readonly record struct WordFilterVerdict(
    string Text,
    bool Refused,
    bool Changed,
    int FirstWordId,
    WordFilterAction FirstAction
);

/// <summary>
/// The compiled word list. Immutable: a reload builds a new one and swaps the reference, so a call in
/// flight sees one whole list or the other, never half of each.
/// </summary>
/// <remarks>
/// Each word becomes one regex over the normalised text: every letter of its key is <c>x+</c> (so a
/// letter may repeat) and letters are joined by <c> *</c> (the normaliser turned separators into
/// spaces), so "con" is <c>c+ *o+ *n+</c>. WholeWord wraps it in <c>\b</c>; that is what keeps
/// "concert" clean.
/// <para>
/// <c>ponytail:</c> one regex per word, tried in turn — O(words × length). Fine for hundreds of words
/// on chat-length lines; a single Aho-Corasick pass over the normalised text is the upgrade if the
/// list reaches thousands.
/// </para>
/// </remarks>
public sealed class WordFilterSet
{
    private readonly ImmutableArray<CompiledWord> _words;

    private WordFilterSet(ImmutableArray<CompiledWord> words) => _words = words;

    public static WordFilterSet Empty { get; } = new([]);

    public int Count => _words.Length;

    public static WordFilterSet Build(
        IEnumerable<WordFilterEntry> entries,
        string defaultReplacement,
        TimeSpan matchTimeout
    )
    {
        ImmutableArray<CompiledWord>.Builder words = ImmutableArray.CreateBuilder<CompiledWord>();

        foreach (WordFilterEntry entry in entries)
        {
            string key = WordFilterNormalizer.Key(entry.Word);

            if (key.Length == 0)
            {
                continue;
            }

            string pattern = string.Join(" *", key.Select(c => Regex.Escape(c.ToString()) + "+"));

            // \b is exactly "not next to [a-z0-9]" here: the normalised text holds nothing but
            // [a-z0-9 ], so there is no underscore or other word character for \b to disagree on.
            if (entry.Match == WordFilterMatch.WholeWord)
            {
                pattern = @"\b" + pattern + @"\b";
            }

            // NonBacktracking: any player can send a 60 000-character line, and this engine is linear
            // in the input by construction. The backtracking engine happens to cope with today's
            // patterns (it makes `a+` atomic), but that is an optimisation it may or may not apply
            // to a future pattern, and a timeout here fails open. It is also why the patterns use
            // \b rather than lookarounds, which this engine does not support.
            words.Add(
                new CompiledWord(
                    entry.Id,
                    entry.Action,
                    entry.Replacement ?? defaultReplacement,
                    new Regex(
                        pattern,
                        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant,
                        matchTimeout
                    )
                )
            );
        }

        return new WordFilterSet(words.ToImmutable());
    }

    /// <exception cref="RegexMatchTimeoutException">A pathological input; the caller fails open.</exception>
    public WordFilterVerdict Evaluate(string text)
    {
        if (_words.IsEmpty || string.IsNullOrEmpty(text))
        {
            return new WordFilterVerdict(text, false, false, 0, default);
        }

        string normalized = WordFilterNormalizer.Normalize(text);
        List<(int Index, int Length, string With)>? spans = null;
        CompiledWord? first = null;

        foreach (CompiledWord word in _words)
        {
            foreach (Match match in word.Pattern.Matches(normalized))
            {
                if (word.Action == WordFilterAction.Refuse)
                {
                    return new WordFilterVerdict(
                        text,
                        true,
                        false,
                        word.Id,
                        WordFilterAction.Refuse
                    );
                }

                first ??= word;
                (spans ??= []).Add((match.Index, match.Length, word.Replacement));
            }
        }

        if (spans is null || first is null)
        {
            return new WordFilterVerdict(text, false, false, 0, default);
        }

        // Earliest first, overlaps dropped (two words matching the same letters replace them once),
        // then applied from the end so earlier indexes stay valid.
        spans.Sort((a, b) => a.Index.CompareTo(b.Index));

        List<(int Index, int Length, string With)> kept = [];
        int end = -1;

        foreach ((int Index, int Length, string With) span in spans)
        {
            if (span.Index >= end)
            {
                kept.Add(span);
                end = span.Index + span.Length;
            }
        }

        StringBuilder result = new(text);

        for (int i = kept.Count - 1; i >= 0; i--)
        {
            result.Remove(kept[i].Index, kept[i].Length).Insert(kept[i].Index, kept[i].With);
        }

        return new WordFilterVerdict(
            result.ToString(),
            false,
            true,
            first.Id,
            WordFilterAction.Replace
        );
    }

    private sealed record CompiledWord(
        int Id,
        WordFilterAction Action,
        string Replacement,
        Regex Pattern
    );
}
