using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>The room's own configuration: settings, rights lists, tags, rating and deletion.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomSettings")]
public interface IRoomSettings : IGrainWithIntegerKey
{
    Task<RoomSnapshot?> GetRoomSettingsAsync(PlayerId actor, CancellationToken ct);

    Task<bool> UpdateRoomSettingsAsync(
        PlayerId actor,
        RoomSettingsUpdate update,
        CancellationToken ct
    );

    /// <summary>
    /// Applies a floor-plan editor save: a new model for the room, and the door, thickness and wall
    /// height that came with it.
    ///
    /// Unlike every other setting this one changes the room's *dimensions*, so it rebuilds the tile
    /// map, re-seats the furniture on it and re-broadcasts the height map to everyone inside.
    /// </summary>
    Task<bool> UpdateFloorPlanAsync(PlayerId actor, FloorPlanUpdate update, CancellationToken ct);

    Task<bool> DeleteRoomAsync(PlayerId actor, CancellationToken ct);

    /// <summary>Owner-only; empty for anyone else. The request names an arbitrary room id, so the
    /// grain is the only place that can decide whether the caller may see the list.</summary>
    Task<ImmutableArray<RoomControllerSnapshot>> GetControllersAsync(
        PlayerId actor,
        CancellationToken ct
    );

    /// <summary>Owner-only; empty for anyone else.</summary>
    Task<ImmutableArray<RoomControllerSnapshot>> GetBannedUsersAsync(
        PlayerId actor,
        CancellationToken ct
    );

    /// <summary>
    /// The room's own banned words, for the filter tab of the room settings dialog. Owner-only, same
    /// reason as the lists above: the request names an arbitrary room id.
    /// </summary>
    Task<ImmutableArray<string>> GetFilterWordsAsync(PlayerId actor, CancellationToken ct);

    /// <summary>
    /// Adds or removes one word from the room's filter and returns the list as it now stands, which
    /// is what the dialog redraws itself from.
    /// </summary>
    /// <param name="adding">True to add the word, false to remove it.</param>
    /// <returns>The current list, or empty when the actor does not own the room.</returns>
    Task<ImmutableArray<string>> UpdateFilterWordAsync(
        PlayerId actor,
        bool adding,
        string word,
        CancellationToken ct
    );

    Task<bool> UpdateCategoryAndTradeAsync(
        PlayerId actor,
        int categoryId,
        RoomTradeModeType tradeType,
        CancellationToken ct
    );

    Task AssignRightsAsync(PlayerId actor, PlayerId target, CancellationToken ct);

    Task RemoveRightsAsync(PlayerId actor, ImmutableArray<PlayerId> targets, CancellationToken ct);

    Task RemoveAllRightsAsync(PlayerId actor, CancellationToken ct);

    Task RemoveOwnRightsAsync(PlayerId actor, CancellationToken ct);

    /// <summary>Owner-only. Max two tags (SetRoomSessionTagsMessage never sends more); null/blank
    /// clears a slot.</summary>
    Task<bool> SetRoomTagsAsync(PlayerId actor, string? tag1, string? tag2, CancellationToken ct);

    /// <summary>
    /// Flips this room's staff pick and answers with the value it now holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A toggle, not a setter, because the client does not send the state it wants. Its button
    /// handler flips only its own caption and then posts back the value it already believed
    /// (<c>RoomInfoViewCtrl.onStaffPick</c> passes <c>currentRoomIsStaffPick</c> unchanged, and
    /// nothing but the room-info packet ever writes that field). Obeying that boolean writes the
    /// value already stored, which is why picking a room did nothing at all until 2026-09-24. The
    /// stored row is the only thing that knows the truth, so the flip happens here.
    /// </para>
    /// <para>
    /// Staff-only: the grain resolves <c>Capabilities.Navigator.StaffPick</c> for
    /// <paramref name="actor"/> and does nothing without it. The handler checks too, to answer the
    /// client without waking a room, but this is the check that decides.
    /// </para>
    /// </remarks>
    /// <returns>The staff pick after the flip, or <c>null</c> when nothing was changed.</returns>
    Task<bool?> ToggleStaffPickAsync(PlayerId actor, CancellationToken ct);

    /// <summary>
    /// Sets this room's staff pick to <paramref name="staffPick"/>. For a caller that knows the
    /// state it wants -- the dashboard, which is looking at the row -- where
    /// <see cref="ToggleStaffPickAsync"/> is for the in-client button, which does not.
    /// </summary>
    /// <returns>Whether the write happened.</returns>
    Task<bool> SetStaffPickAsync(PlayerId actor, bool staffPick, CancellationToken ct);
}
