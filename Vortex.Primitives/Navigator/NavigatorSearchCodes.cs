using System.Collections.Generic;
using System.Collections.Immutable;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Primitives.Navigator;

/// <summary>
/// The search codes the client sends with every navigator request, and the query each one means.
/// </summary>
/// <remarks>
/// <para>
/// The codes are hardcoded client-side (one per navigator tab/button), so the server must know them
/// without help from the database. <c>navigator_top_level_contexts</c>/<c>navigator_quick_links</c>
/// stay authoritative -- an operator can repoint a code at a different query -- but a missing or
/// unseeded row must not silently turn a search into "every room in the hotel".
/// </para>
/// <para>
/// The spellings are the keys the client localizes a result block's header with
/// (<c>navigator.searchcode.title.&lt;code&gt;</c> in <c>external_texts.json</c>). A block sent under
/// a code that has no such key renders with the raw code as its title.
/// </para>
/// <para>
/// The navigator serves my rooms, my friends' rooms, my groups' bases, every guild base and a text
/// search. Every other code the client knows (official, popular, history, favourites, ...) is not
/// served and answers nothing.
/// </para>
/// </remarks>
public static class NavigatorSearchCodes
{
    public const string HotelView = "hotel_view";
    public const string MyWorldView = "myworld_view";

    public const string FriendsRooms = "friends_rooms";
    public const string GuildBases = "groups";
    public const string MyGuildBases = "my_groups";
    public const string MyRooms = "my";
    public const string TextSearch = "query";

    public static readonly ImmutableHashSet<string> TopLevelViews = ImmutableHashSet.Create(
        HotelView,
        MyWorldView
    );

    public static readonly ImmutableDictionary<string, NavigatorQueryType> QueryTypeBySearchCode =
        ImmutableDictionary.CreateRange([
            KeyValuePair.Create(FriendsRooms, NavigatorQueryType.FriendsRooms),
            KeyValuePair.Create(GuildBases, NavigatorQueryType.GuildBases),
            // The hotel view is a search box: it lists nothing until a text is typed.
            KeyValuePair.Create(HotelView, NavigatorQueryType.TextSearch),
            KeyValuePair.Create(MyGuildBases, NavigatorQueryType.MyGroups),
            KeyValuePair.Create(MyRooms, NavigatorQueryType.MyRooms),
            KeyValuePair.Create(MyWorldView, NavigatorQueryType.MyRooms),
            KeyValuePair.Create(TextSearch, NavigatorQueryType.TextSearch),
        ]);
}
