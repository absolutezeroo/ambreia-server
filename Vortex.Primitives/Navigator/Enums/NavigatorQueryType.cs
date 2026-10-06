namespace Vortex.Primitives.Navigator.Enums;

/// <summary>
/// What a navigator search code asks for. The numbers are persisted (the <c>query_type</c> column of
/// the top-level-context and quick-link rows), so a value removed in M0 leaves a gap rather than
/// renumbering: 2 (favourites), 4 (rooms with friends), 5/6 (history), 7 (rights), 8 (room ads),
/// 9 (by flat category), 10-13 (popular, highest score, recommended, staff picks), 17 (competition)
/// and 18 (event category). A stored row that still carries one of them resolves to no query.
/// </summary>
public enum NavigatorQueryType
{
    /// <summary>No query: the code is not served, and the search answers nothing. This is the
    /// default of an unconfigured row, never "every room".</summary>
    None = 0,
    MyRooms = 1,
    FriendsRooms = 3,
    TextSearch = 14,
    MyGroups = 15,
    GuildBases = 16,
}
