using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;

namespace Vortex.RewardTracks.Progression;

/// <summary>
/// What each satisfied step of a sequence matched, so a later step can point back at it.
/// </summary>
/// <remarks>
/// <para>
/// This is what "walk on the furniture you just placed" is made of. Every step records all of its
/// facts on the way past, and a filter written <c>$1</c> reads step 1's value for the same fact
/// key — which is why capturing needs no separate row in the editor.
/// </para>
/// <para>
/// Stored as one string on the player's task row, like the distinct-key set beside it, because it
/// is small, private to one task, and never queried. It is bounded by the content: a sequence has
/// as many steps as an operator wrote, and each records the handful of facts its action emits.
/// </para>
/// </remarks>
internal readonly struct StepCaptures
{
    // Shared with the dashboard, which reads this row to show what a player has matched so far.
    private const char PairSeparator = RewardTrackCaptureFormat.PairSeparator;
    private const char FieldSeparator = RewardTrackCaptureFormat.FieldSeparator;

    private readonly Dictionary<string, string>? _byStepAndKey;

    private StepCaptures(Dictionary<string, string>? values) => _byStepAndKey = values;

    /// <summary>Reads the stored form. An unparseable entry is skipped, never thrown on.</summary>
    public static StepCaptures Parse(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return new StepCaptures(null);
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string entry in stored.Split(PairSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split(FieldSeparator);

            if (parts.Length == 3)
            {
                values[Key(parts[0], parts[1])] = parts[2];
            }
        }

        return new StepCaptures(values);
    }

    /// <summary>What the given step matched for the given fact, or null if it recorded none.</summary>
    public string? Get(int stepIndex, string factKey) =>
        _byStepAndKey?.GetValueOrDefault(
            Key(stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), factKey)
        );

    /// <summary>This set plus everything one step just matched.</summary>
    public StepCaptures With(int stepIndex, ImmutableArray<RewardTrackFactSnapshot> facts)
    {
        Dictionary<string, string> values = _byStepAndKey is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(_byStepAndKey, StringComparer.Ordinal);

        if (!facts.IsDefaultOrEmpty)
        {
            string step = stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

            foreach (RewardTrackFactSnapshot fact in facts)
            {
                // A value carrying a separator would corrupt the row on the way back in. Facts are
                // ids and short codes, so this never fires in practice -- it is here so that a fact
                // that one day carries free text cannot silently break every sequence.
                if (fact.Value.IndexOf(PairSeparator) < 0 && fact.Value.IndexOf(FieldSeparator) < 0)
                {
                    values[Key(step, fact.Key)] = fact.Value;
                }
            }
        }

        return new StepCaptures(values);
    }

    /// <summary>The stored form.</summary>
    public string Serialize()
    {
        if (_byStepAndKey is null || _byStepAndKey.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();

        foreach (KeyValuePair<string, string> pair in _byStepAndKey)
        {
            if (builder.Length > 0)
            {
                builder.Append(PairSeparator);
            }

            builder.Append(pair.Key).Append(FieldSeparator).Append(pair.Value);
        }

        return builder.ToString();
    }

    private static string Key(string step, string factKey) => step + FieldSeparator + factKey;
}
