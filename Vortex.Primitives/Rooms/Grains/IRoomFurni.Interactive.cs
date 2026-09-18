using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;

namespace Vortex.Primitives.Rooms.Grains;

public partial interface IRoomFurni
{
    /// <summary>
    /// Dresses a mannequin in the given look. The figure comes from the caller, not the packet —
    /// the client sends only which mannequin, and the server reads the requester's own avatar, so
    /// nobody can put an arbitrary figure on someone else's furniture.
    /// </summary>
    /// <returns>False if the item is absent, is not a mannequin, or the actor lacks room rights.</returns>
    public Task<bool> SetMannequinOutfitAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string figure,
        string gender,
        CancellationToken ct
    );

    /// <summary>Renames a mannequin's saved outfit.</summary>
    public Task<bool> SetMannequinNameAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string name,
        CancellationToken ct
    );

    /// <summary>
    /// Reads a furni's data as the client reads it: one legacy string.
    /// </summary>
    /// <remarks>
    /// The sticky note asks for this the moment it is opened, before anything is drawn, so an
    /// unanswered request is a note that stays blank. Null when the item is gone or the caller may
    /// not touch it; an item that simply has nothing written on it answers with an empty string,
    /// which is a different thing and the client draws it as a blank note rather than not at all.
    /// </remarks>
    public Task<string?> GetItemDataAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Sets one gender's outfit on a clothing-change booth, keeping the other.
    /// </summary>
    /// <remarks>
    /// The booth holds both looks in one string as <c>"&lt;boy&gt;,&lt;girl&gt;"</c> and the client
    /// takes the half matching whoever is standing in front of it. One message carries one gender,
    /// so this merges: overwriting would silently clear the outfit nobody was editing.
    /// </remarks>
    public Task<bool> SetClothingChangeDataAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string gender,
        string look,
        CancellationToken ct
    );

    /// <summary>
    /// Writes named fields onto a furni whose editor is a set of keys rather than a single note.
    /// </summary>
    /// <remarks>
    /// Merged into whatever the furni already holds, key by key: the client sends only the fields
    /// its editor showed, and a furni's data usually carries more than one editor writes.
    /// </remarks>
    public Task<bool> SetObjectDataAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        IReadOnlyList<(string Key, string Value)> pairs,
        CancellationToken ct
    );

    /// <summary>
    /// Writes a sticky note's paper colour and text. Both travel together in one legacy string, so
    /// there is no way to change one without rewriting the other.
    /// </summary>
    public Task<bool> SetPostItAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string colorHex,
        string text,
        CancellationToken ct
    );

    /// <summary>
    /// Raises or lowers a magic stack tile, which is what everything stacked on it then sits on.
    /// </summary>
    /// <param name="heightHundredths">
    /// Hundredths of a tile, as the widget sends it. <c>-100</c> is its "back to normal" button and
    /// drops the tile onto whatever is naturally under it.
    /// </param>
    /// <param name="multiWalk">
    /// The multi-walk checkbox, absent from the packet unless it is what moved.
    /// </param>
    /// <returns>False if the item is absent, is not a magic tile, the actor lacks room rights, or
    /// the requested height is out of range.</returns>
    public Task<bool> SetCustomStackHeightAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int heightHundredths,
        bool? multiWalk,
        CancellationToken ct
    );

    /// <summary>
    /// Throws away a wall item that is meant to be thrown away — a sticky note or a photo.
    /// </summary>
    /// <remarks>
    /// Deliberately not "delete any wall item the packet names". The client only offers this on its
    /// sticky and photo widgets, but the packet is one integer and a crafted one would otherwise let
    /// anybody with build rights destroy a visitor's wall furniture with no way to get it back.
    /// Everything else is picked up, which returns it to its owner.
    /// </remarks>
    /// <returns>False if the item is absent, is not disposable, or the actor lacks room rights.</returns>
    public Task<bool> DeleteDisposableWallItemAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Walks the actor through a one-way gate: onto the tile behind it, facing the way they came
    /// out. Does nothing unless they are standing on the gate's own tile and the tile behind it can
    /// be walked onto.
    /// </summary>
    /// <returns>False if the item is absent, is not a gate, the actor is not on it, or the far side
    /// is blocked.</returns>
    public Task<bool> EnterOneWayDoorAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Tints the room from a background toner. Room rights, not ownership: the toner colours
    /// everyone's view, so it belongs to whoever may build here.
    /// </summary>
    public Task<bool> SetBackgroundColorAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int hue,
        int saturation,
        int lightness,
        CancellationToken ct
    );

    /// <summary>
    /// Cashes in a credit furni and consumes it. Returns what it was worth and in which currency,
    /// for the caller to pay out, or <see cref="CreditFurniPayout.None" /> when the item is absent,
    /// is not the actor's own, or names no value.
    /// </summary>
    /// <remarks>
    /// Two conventions, not one: a <c>CF_*</c> coin pays credits, an <c>nft_emerald*</c> one pays
    /// emeralds. The client already tells them apart — it runs a different furniture logic and
    /// prints a different prompt — so answering with credits for both would pay the wrong purse for
    /// half of them.
    /// </remarks>
    public Task<CreditFurniPayout> RedeemCreditFurniAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Unwraps a present and consumes it. Returns what was inside, for the caller to grant, or null
    /// when the item is absent, is not a present, is not the actor's own, or holds nothing the
    /// server can resolve.
    /// </summary>
    /// <remarks>
    /// Consuming and granting are split across two grains on purpose, in that order: the wrapping is
    /// gone before anything is handed out, so a failure downstream costs the player their gift once
    /// rather than letting a repeated click on a present that is still there mint copies of it.
    /// </remarks>
    public Task<PresentContentsSnapshot?> OpenPresentAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>Reads a moodlight for its dialog. Null when the item is absent, is not a moodlight,
    /// or the actor lacks room rights.</summary>
    public Task<RoomDimmerStateSnapshot?> GetDimmerStateAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Flips a moodlight on or off and returns what the dialog should now show.
    /// </summary>
    public Task<RoomDimmerStateSnapshot?> ToggleDimmerAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Overwrites one of a moodlight's three presets, switching to it when <paramref name="apply"/>
    /// is set, and returns what the dialog should now show.
    /// </summary>
    public Task<RoomDimmerStateSnapshot?> SaveDimmerPresetAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int presetNumber,
        int effectId,
        string colorHex,
        int brightness,
        bool apply,
        CancellationToken ct
    );
}
