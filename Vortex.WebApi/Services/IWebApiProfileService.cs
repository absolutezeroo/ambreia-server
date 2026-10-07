using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

/// <summary>
/// One badge a player owns. <paramref name="BadgeIndex"/> is the slot it is worn in (1-5 on the
/// avatar, 0 when it is only owned), matching <c>PlayerBadgeEntity.SlotId</c>.
/// </summary>
/// <remarks>
/// habbo.com's own profile payload carries a <c>name</c> and a <c>description</c> beside the code,
/// because its CMS holds the badge texts. This hotel's do not live in the database at all: they are
/// <c>badge_&lt;code&gt;_name</c> / <c>badge_&lt;code&gt;_desc</c> in
/// <c>gamedata/&lt;lang&gt;/external_flash_texts.json</c> on the asset host, which is where the
/// CLIENT reads them from too. Serving an empty string from here would look like data and read as a
/// missing badge; the site resolves the code against that file, one fetch, cached.
/// </remarks>
public sealed record ProfileBadge(int BadgeIndex, string Code);

/// <summary>A friend as the profile page lists them — enough to draw a head and link to it.</summary>
public sealed record ProfileFriend(
    string UniqueId,
    string Name,
    string FigureString,
    string Motto,
    bool Online
);

/// <summary>A room the player owns.</summary>
public sealed record ProfileRoom(
    int Id,
    string Name,
    string Description,
    int UsersNow,
    int MaximumVisitors
);

/// <summary>
/// A group the player belongs to. <paramref name="IsAdmin"/> covers both the owner and an
/// administrator, which is the distinction habbo.com's profile draws.
/// </summary>
public sealed record ProfileGroup(
    int Id,
    string Name,
    string Description,
    int RoomId,
    string BadgeCode,
    string PrimaryColour,
    string SecondaryColour,
    bool IsAdmin
);

/// <summary>
/// The profile header: who this is, since when, and the badges they chose to wear.
/// </summary>
/// <remarks>
/// <paramref name="ProfileVisible"/> is <c>PlayerEntity.ProfileVisible</c>, false by default — the
/// promise the registration form makes in habbo.com's own words. A profile that is not visible
/// answers with this header and four empty lists; it is never a 404, because the player does exist
/// and a 404 would turn this route into a way to test whether a name is taken.
/// </remarks>
public sealed record ProfileUser(
    string UniqueId,
    string Name,
    string FigureString,
    string Motto,
    DateTime MemberSince,
    bool ProfileVisible,
    bool Online,
    int AchievementScore,
    // Always 0: figé, retiré avec le client -- respects were removed (M0 bis); the site still reads it.
    int RespectReceived,
    IReadOnlyList<ProfileBadge> SelectedBadges
);

/// <summary>
/// What <c>GET /api/public/users/{uniqueId}/profile</c> answers, in habbo.com's own shape: the user
/// plus the four lists the profile page draws a card from.
/// </summary>
public sealed record PlayerProfile(
    ProfileUser User,
    IReadOnlyList<ProfileBadge> Badges,
    IReadOnlyList<ProfileFriend> Friends,
    IReadOnlyList<ProfileRoom> Rooms,
    IReadOnlyList<ProfileGroup> Groups
);

/// <summary>
/// The profile reads. The two public ones are anonymous by design — a profile is a page a visitor
/// can open, and the site links to one from every friend head and every article author — and the
/// third belongs to whoever is signed in.
/// </summary>
public interface IWebApiProfileService
{
    /// <summary>
    /// Resolves a name to its user header, the way habbo.com's <c>/api/public/users?name=</c> does —
    /// the site routes on <c>/profile/:name</c> and the profile read takes an id.
    /// </summary>
    Task<ProfileUser?> FindUserByNameAsync(string name, CancellationToken ct);

    /// <summary>
    /// The full profile payload as a VISITOR sees it, or <c>null</c> when no such player exists. A
    /// player who has hidden their profile answers with the header and four empty lists.
    /// </summary>
    Task<PlayerProfile?> GetProfileAsync(int playerId, CancellationToken ct);

    /// <summary>The same payload as its OWNER sees it: the visibility flag is not applied.</summary>
    /// <remarks>
    /// habbo.com resolves its own profile page through <c>/api/user/profile</c> rather than the
    /// public route, and this is why — a player who hides their profile still has one, and telling
    /// them "this profile is private" about themselves is the shape of bug that makes a setting look
    /// like a malfunction. Establishing WHOSE profile it is belongs to the caller: the endpoint
    /// answers the session's selected avatar and takes no id.
    /// </remarks>
    Task<PlayerProfile?> GetOwnProfileAsync(int playerId, CancellationToken ct);
}
