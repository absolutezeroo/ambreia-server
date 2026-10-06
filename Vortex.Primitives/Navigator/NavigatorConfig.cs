namespace Vortex.Primitives.Navigator;

/// <summary>
/// Config keys and defaults for navigator search, served live from <c>IServerConfigGrain</c>.
/// The default is the fallback when a key has no admin override stored in the DB.
/// </summary>
/// <remarks>
/// Every navigator query used to be unbounded: a search returned one <c>RoomInfoSnapshot</c> per
/// matching row in <c>rooms</c>, which is fine on a dev hotel and a full table scan serialized to a
/// client on a real one. The cap lives here rather than as a constant in the provider so an operator
/// can tune it without a redeploy.
/// </remarks>
public static class NavigatorConfig
{
    /// <summary>Rooms returned by a single navigator search block.</summary>
    public const string SearchResultLimitKey = "navigator.search_result_limit";
    public const int SearchResultLimitDefault = 50;
}
