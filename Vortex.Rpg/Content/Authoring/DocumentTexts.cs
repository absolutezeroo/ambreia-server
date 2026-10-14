using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>
/// A document's lines in a text file (content authoring §3, §5): the keys starting with its id and a
/// dot, minus those of a longer id nested under it. They are replaced where the first one stood, the
/// rest of the file (comments, order, other documents) kept as it is.
/// </summary>
public static partial class DocumentTexts
{
    [GeneratedRegex("^[a-z]{2}(-[a-z]{2})?$")]
    private static partial Regex Language();

    /// <summary>Every language a code, every key the document's, nothing that would break a
    /// <c>key=value</c> line.</summary>
    public static bool IsValid(
        string id,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts
    ) =>
        texts.All(language =>
            Language().IsMatch(language.Key)
            && language.Value.All(text =>
                text.Key.Length > id.Length + 1
                && text.Key.StartsWith(id + ".", StringComparison.Ordinal)
                && text.Key.All(c => c is not ('=' or '#' or '\r' or '\n') && !char.IsWhiteSpace(c))
                && !text.Value.Contains('\r')
                && !text.Value.Contains('\n')
            )
        );

    /// <summary>The body with the document's lines replaced by <paramref name="lines"/> (none to drop them).</summary>
    public static string Rewrite(
        string body,
        string id,
        IReadOnlyCollection<string> nested,
        IReadOnlyDictionary<string, string> lines
    )
    {
        List<string> kept = [];
        int first = -1;

        foreach (string line in body.Split('\n'))
        {
            if (Owns(line, id, nested))
            {
                first = first < 0 ? kept.Count : first;
                continue;
            }

            kept.Add(line);
        }

        // No line of the document yet: before the file's final newline.
        int at =
            first >= 0 ? first
            : kept.Count > 0 && kept[^1] == string.Empty ? kept.Count - 1
            : kept.Count;

        kept.InsertRange(at, lines.Select(l => $"{l.Key}={l.Value}"));

        return string.Join('\n', kept);
    }

    private static bool Owns(string line, string id, IReadOnlyCollection<string> nested)
    {
        int equals = line.IndexOf('=');

        if (equals <= 0 || line.TrimStart().StartsWith('#'))
        {
            return false;
        }

        string key = line[..equals].Trim();

        return key.StartsWith(id + ".", StringComparison.Ordinal)
            && !nested.Any(other => key.StartsWith(other + ".", StringComparison.Ordinal));
    }
}
