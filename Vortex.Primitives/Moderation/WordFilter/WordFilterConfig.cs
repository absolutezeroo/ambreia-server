using System.Collections.Immutable;

namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>
/// The filter's two live settings, served by <c>IServerConfigGrain</c>. Read when the list compiles,
/// never per call, so a change lands on the next reload — and any edit of the list triggers one.
/// </summary>
public static class WordFilterConfig
{
    public const string ReplacementKey = "word_filter.replacement";
    public const string ReplacementDefault = "bobba";

    public const string RefusedMessageKey = "word_filter.refused_message";
    public const string RefusedMessageDefault = "Ton message contient un mot interdit.";

    /// <summary>Longest word or replacement accepted. Both columns hold 64.</summary>
    public const int WordMaxLength = 64;

    /// <summary>Longest original text the journal keeps.</summary>
    public const int JournalTextMaxLength = 512;

    public static readonly ImmutableArray<string> Keys = [ReplacementKey, RefusedMessageKey];
}
