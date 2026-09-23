namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>
/// Where a filtered text was written — the journal's "source" column. Persisted as its number, so
/// append only. The dashboard's journal names each one (<c>wordFilter.sources.N</c> in its locales).
/// </summary>
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

    /// <summary>A room's name, description or tags.</summary>
    Room = 8,

    /// <summary>A room event's (promotion's) name or description.</summary>
    RoomEvent = 9,

    /// <summary>A pet's name.</summary>
    Pet = 10,

    /// <summary>A bot's name or the lines it speaks.</summary>
    Bot = 11,

    /// <summary>A post-it's text.</summary>
    PostIt = 12,

    /// <summary>A mannequin's name.</summary>
    Mannequin = 13,

    /// <summary>A gift card's message.</summary>
    Gift = 14,

    /// <summary>Wired text: a message a box shows or a bot says, a chest's or a contract's texts.</summary>
    Wired = 15,

    /// <summary>A guide session's chat or a help request's description.</summary>
    Guide = 16,
}
