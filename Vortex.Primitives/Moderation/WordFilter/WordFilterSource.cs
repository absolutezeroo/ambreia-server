namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>
/// Where a filtered text was written — the journal's "source" column. Persisted as its number, so
/// append only. The dashboard's journal names each one (<c>wordFilter.sources.N</c> in its locales).
/// </summary>
/// <remarks>
/// Some sources are keys rather than prose — a name, a search tag — and a replacement spliced into
/// a key is worse than no text at all: a pet called "bobba", a tag nobody searches for, a bot the
/// wired can no longer find by name. For those, <see cref="WordFilterSourceRules.IsStrict"/> makes
/// any hit a refusal.
/// </remarks>
public enum WordFilterSource
{
    RoomChat = 0,
    Messenger = 1,
    RoomInvite = 2,
    Motto = 3,
    Username = 4,
    FeedPost = 5,

    /// <summary>A group's name or description, at creation or edit.</summary>
    Group = 6,

    /// <summary>A group forum's thread subject or post.</summary>
    Forum = 7,

    /// <summary>A room's name or description.</summary>
    Room = 8,

    /// <summary>A room's navigator tag. Strict: a tag is a search key.</summary>
    RoomTag = 9,

    /// <summary>A room event's (promotion's) name or description.</summary>
    RoomEvent = 10,

    /// <summary>A pet's name, at purchase. Strict: a name.</summary>
    PetName = 11,

    /// <summary>A bot's name. Strict: the wired bot actions find a bot by it.</summary>
    BotName = 12,

    /// <summary>The lines a bot speaks on its own.</summary>
    BotChatter = 13,

    /// <summary>A post-it's text.</summary>
    PostIt = 14,

    /// <summary>A mannequin's name.</summary>
    Mannequin = 15,

    /// <summary>A gift card's message.</summary>
    Gift = 16,

    /// <summary>Wired text: a message a box shows or a bot says, a chest's or a contract's texts.</summary>
    Wired = 17,

    /// <summary>A guide session's chat or a help request's description.</summary>
    Guide = 18,

    /// <summary>A website photo album's title or description.</summary>
    Album = 19,
}

/// <summary>What each source means for the filter's outcome.</summary>
public static class WordFilterSourceRules
{
    /// <summary>A key, not prose: any hit refuses, whatever the word's action.</summary>
    public static bool IsStrict(WordFilterSource source) =>
        source
            is WordFilterSource.Username
                or WordFilterSource.RoomTag
                or WordFilterSource.PetName
                or WordFilterSource.BotName;
}
