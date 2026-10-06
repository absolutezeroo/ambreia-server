using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Groups.Grains;

/// <summary>
/// Global (singleton) grain for group operations that are not scoped to a single existing group:
/// creation, the creation wizard data, and a player's membership list.
/// </summary>
public interface IGroupDirectoryGrain : IGrainWithStringKey
{
    Task<GroupCreationInfoSnapshot> GetCreationInfoAsync(PlayerId player, CancellationToken ct);

    /// <summary>
    /// Creates a group on <paramref name="baseRoomId"/> (which <paramref name="owner"/> must own and
    /// which must not already be a guild base). Returns the new group id, or null on validation
    /// failure.
    /// </summary>
    Task<int?> CreateGroupAsync(
        PlayerId owner,
        string name,
        string description,
        int primaryColorId,
        int secondaryColorId,
        int baseRoomId,
        IReadOnlyList<int> badgeParts,
        CancellationToken ct
    );

    /// <summary>Groups the player owns or belongs to, for the "my groups" list.</summary>
    Task<List<GuildInfoSnapshot>> GetMembershipsAsync(PlayerId player, CancellationToken ct);

    /// <summary>Sets or clears the player's favourite guild (favourite == false clears it).</summary>
    Task SetFavouriteAsync(PlayerId player, int groupId, bool favourite, CancellationToken ct);

    /// <summary>Badge editor palettes (base/layer parts and color swatches).</summary>
    Task<GroupEditorDataSnapshot> GetEditorDataAsync(CancellationToken ct);

    /// <summary>Badge codes for the player's groups, for the client badge cache.</summary>
    Task<List<GroupBadgeSnapshot>> GetBadgesAsync(PlayerId player, CancellationToken ct);

    /// <summary>A page of forums to browse (cross-group). <paramref name="listCode"/> = sort mode.</summary>
    Task<ForumsListPageSnapshot> GetForumsListAsync(
        PlayerId player,
        int listCode,
        int startIndex,
        int amount,
        CancellationToken ct
    );

    /// <summary>Number of the player's group forums that have unread activity.</summary>
    /// <summary>
    /// The guild's visual identity as guild-customized furni needs it: badge code plus both recolour
    /// slots already resolved to hex. Returns null if the guild does not exist, or if
    /// <paramref name="player"/> is not a member — buying guild furni for a guild you left must not
    /// stamp that guild's badge onto it.
    /// </summary>
    Task<GuildFurniIdentitySnapshot?> GetFurniIdentityAsync(
        PlayerId player,
        int groupId,
        CancellationToken ct
    );

    Task<int> GetUnreadForumsCountAsync(PlayerId player, CancellationToken ct);

    /// <summary>
    /// Records how far the player has read into each listed forum. Markers only ever move forward,
    /// so a stale batch arriving late cannot resurrect already-read messages as unread.
    /// </summary>
    Task UpdateForumReadMarkersAsync(
        PlayerId player,
        IReadOnlyList<ForumReadMarkerUpdate> markers,
        CancellationToken ct
    );
}
