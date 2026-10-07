namespace Vortex.Primitives.Groups;

/// <summary>
/// Config keys and defaults for guild/group limits, served live from <c>IServerConfigGrain</c>
/// (migrated off IOptions/appsettings). The default is the fallback when a key has no admin override
/// stored in the DB.
/// </summary>
public static class GroupConfig
{
    /// <summary>Members shown per page in the group member list.</summary>
    public const string MembersPerPageKey = "group.members_per_page";
    public const int MembersPerPageDefault = 14;

    /// <summary>Credit cost charged to create a guild.</summary>
    public const string CreationCostInCreditsKey = "group.creation_cost_credits";
    public const int CreationCostInCreditsDefault = 10;

    /// <summary>Upper bound on the forum page size a client may request.</summary>
    public const string MaxForumPageSizeKey = "group.max_forum_page_size";
    public const int MaxForumPageSizeDefault = 50;

    /// <summary>Forum page size used when the request is out of range.</summary>
    public const string DefaultForumPageSizeKey = "group.default_forum_page_size";
    public const int DefaultForumPageSizeDefault = 20;

    /// <summary>Maximum allowed length of a guild name.</summary>
    public const string MaxNameLengthKey = "group.max_name_length";
    public const int MaxNameLengthDefault = 50;
}
