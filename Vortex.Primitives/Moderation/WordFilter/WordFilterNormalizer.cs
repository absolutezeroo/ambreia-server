using System.Text;

namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>
/// The one normalisation both sides of the filter go through: a listed word at compile time, and
/// every filtered text per call. Applying the same fold to both is what makes a disguised spelling
/// land on the same letters as the word it disguises.
/// </summary>
/// <remarks>
/// One output character per input character, always. A match found in the normalised text is
/// therefore the same span in the original, which is what lets a replacement swap exactly the
/// offending letters and leave the punctuation, emoji and casing around them alone.
/// <para>
/// <c>!</c> is deliberately not leet for <c>i</c>: "con!" would fold to "coni" and lose the word
/// boundary a WholeWord entry needs.
/// </para>
/// <para>
/// <c>ponytail:</c> accents are folded through NFKD, which covers Latin diacritics and compatibility
/// forms but not look-alike letters from other scripts (Cyrillic <c>о</c> for <c>o</c>). A
/// confusables table in <see cref="Fold"/> is the upgrade path if that becomes the workaround.
/// </para>
/// </remarks>
public static class WordFilterNormalizer
{
    public static string Normalize(string text) =>
        string.Create(
            text.Length,
            text,
            static (span, source) =>
            {
                for (int i = 0; i < source.Length; i++)
                {
                    span[i] = Fold(source[i]);
                }
            }
        );

    /// <summary>
    /// A word's identity, and the letters its pattern is built from: normalised, separators dropped.
    /// Two words with the same key are the same entry — "CON" and "c.0.n" cannot both be listed.
    /// </summary>
    /// <remarks>
    /// Doubled letters are kept. The matcher already lets any letter repeat in the text
    /// ("con" catches "cooon"), so collapsing them here would only lose words: "ass" would become
    /// "as" and ban the French word.
    /// </remarks>
    public static string Key(string word)
    {
        string normalized = Normalize(word.Trim());
        StringBuilder key = new(normalized.Length);

        foreach (char c in normalized)
        {
            if (c != ' ')
            {
                key.Append(c);
            }
        }

        return key.ToString();
    }

    private static char Fold(char c)
    {
        // Half of an emoji's surrogate pair on its own is not a valid string for NFKD, which throws
        // on it — and every emoji in a chat line is two of them. Neither half is a letter anyway.
        if (char.IsSurrogate(c))
        {
            return ' ';
        }

        if (c > 127)
        {
            string decomposed = c.ToString().Normalize(NormalizationForm.FormKD);
            c = decomposed.Length > 0 ? decomposed[0] : c;
        }

        c = char.ToLowerInvariant(c);

        c = c switch
        {
            '4' or '@' => 'a',
            '3' => 'e',
            '1' => 'i',
            '0' => 'o',
            '5' or '$' => 's',
            '7' => 't',
            _ => c,
        };

        return c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : ' ';
    }
}
