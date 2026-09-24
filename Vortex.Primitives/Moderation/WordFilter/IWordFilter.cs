using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>
/// The hotel-wide word filter. Staff keep the list in the dashboard; every sink where a player
/// writes text another player will read calls <see cref="Apply"/> before storing or sending it.
/// </summary>
/// <remarks>
/// Synchronous on purpose: it sits on the chat path, touches no I/O (the journal is an enqueue),
/// and a grain or a web service can call it without another hop.
/// </remarks>
public interface IWordFilter
{
    /// <summary>
    /// Filters <paramref name="text"/> and journals any hit against the player. A key source — a
    /// name, a search tag, see <see cref="WordFilterSourceRules.IsStrict"/> — is refused on any hit,
    /// whatever the word's action: a key with "bobba" spliced into it is not one anyone chose.
    /// </summary>
    WordFilterResult Apply(string text, WordFilterSource source, int playerId);

    /// <summary>
    /// What <see cref="Apply"/> would answer, journaling nothing. For probes that fire on every
    /// keystroke (the name check) and the dashboard's try-a-sentence box.
    /// </summary>
    WordFilterResult Test(string text);

    /// <summary>The refusal notification's text, from <c>word_filter.refused_message</c>.</summary>
    string RefusedMessage { get; }

    /// <summary>Re-reads the list and both config keys, and swaps the compiled set atomically.</summary>
    Task ReloadAsync(CancellationToken ct);
}

/// <summary>
/// <paramref name="Text"/> is the text to use (the same instance when nothing matched);
/// <paramref name="Refused"/> means use nothing at all; <paramref name="Changed"/> means at least
/// one span was replaced.
/// </summary>
public readonly record struct WordFilterResult(string Text, bool Refused, bool Changed)
{
    /// <summary>Any hit at all. A name treats a replacement as a refusal.</summary>
    public bool Hit => Refused || Changed;
}
