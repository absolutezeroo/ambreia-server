using System.Collections.Immutable;

namespace Vortex.Rooms.Configuration;

/// <summary>
/// The room chat filter's three numbers, served live from <c>IServerConfigGrain</c> so an operator
/// can change them without a deploy. Key/default pairs in the same shape as <see cref="RespectConfig"/>
/// over in the players module; the dashboard editor learns about them from <c>ConfigKeyCatalog</c>.
/// </summary>
/// <remarks>
/// The two limits are read where they are used, in the grain call an edit already makes. The
/// replacement is not: it is needed on the chat path, once per line, so it is read when the room
/// loads its word list and again whenever that list is edited. A change to it therefore reaches a
/// room already awake only on its next filter edit or its next load — which is the price of keeping
/// a grain call off every chat line.
/// </remarks>
public static class RoomFilterConfig
{
    /// <summary>How many words one room's filter may hold.</summary>
    public const string MaxWordsKey = "room.filter.max_words";
    public const int MaxWordsDefault = 50;

    /// <summary>Longest filtered word accepted. The column holds 64.</summary>
    public const string WordMaxLengthKey = "room.filter.word_max_length";
    public const int WordMaxLengthDefault = 64;

    /// <summary>What a filtered word is replaced with in the line everyone sees.</summary>
    public const string ReplacementKey = "room.filter.replacement";
    public const string ReplacementDefault = "bobba";

    /// <summary>All three, for the one batched read an edit makes.</summary>
    public static readonly ImmutableArray<string> Keys =
    [
        MaxWordsKey,
        WordMaxLengthKey,
        ReplacementKey,
    ];
}
