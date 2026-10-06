using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Inventory.Furniture;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Primitives.Sound.Snapshots;

namespace Vortex.Primitives.Inventory.Grains;

public partial interface IInventoryGrain
{
    /// <summary>
    /// Puts a bought offer into a present furniture in this player's inventory instead of granting
    /// it outright. Returns false when the wrapping names no furniture this hotel ships, so the
    /// caller can still deliver the gift unwrapped rather than lose a paid purchase.
    /// </summary>
    public Task<bool> GrantWrappedGiftAsync(
        CatalogOfferSnapshot offer,
        string extraParam,
        GiftWrappingSpec wrapping,
        string purchaserName,
        string purchaserFigure,
        CancellationToken ct
    );
}

public partial interface IInventoryGrain
{
    public Task<bool> AddFurnitureAsync(IFurnitureItem item, CancellationToken ct);
    public Task<bool> AddFurnitureFromRoomItemSnapshotAsync(
        RoomItemSnapshot snapshot,
        CancellationToken ct
    );
    public Task<bool> RemoveFurnitureAsync(RoomObjectId itemId, CancellationToken ct);

    /// <summary>
    /// Rebuilds the furniture list from the database.
    /// </summary>
    /// <remarks>
    /// For ownership that changed somewhere other than here — a trade committing, an admin
    /// revoke or transfer. The list is a cache built once at activation, so without this the player
    /// keeps seeing what they owned a moment ago. It reloads everything rather than the rows that
    /// moved: the same trade-off the trade path already takes, and one query beats bookkeeping.
    /// </remarks>
    public Task ReloadFurnitureAsync(CancellationToken ct);

    public Task GrantCatalogOfferAsync(
        CatalogOfferSnapshot offer,
        string extraParam,
        int quantity,
        CancellationToken ct
    );
    public Task<FurnitureItemSnapshot?> GetItemSnapshotAsync(
        RoomObjectId itemId,
        CancellationToken ct
    );
    public Task<ImmutableArray<FurnitureItemSnapshot>> GetAllItemSnapshotsAsync(
        CancellationToken ct
    );

    /// <summary>
    /// The song disks the player is holding, each with the song pressed on it.
    /// </summary>
    /// <remarks>
    /// The filtering belongs here rather than in the caller: what makes an item a song disk is its
    /// definition's logic name, and the song it carries is its extra data read as a number. Both are
    /// facts about the item, and the handler that asks this question has no business knowing either.
    /// </remarks>
    public Task<ImmutableArray<SongDiskSnapshot>> GetSongDisksAsync(CancellationToken ct);

    public Task GrantBadgeAsync(string badgeCode, CancellationToken ct);

    /// <summary>
    /// Removes a badge the player owns (no-op if absent). Used when an achievement level-up replaces
    /// the previous level's badge with the new one.
    /// </summary>
    public Task RemoveBadgeAsync(string badgeCode, CancellationToken ct);

    public Task GrantFurnitureDefinitionAsync(
        int definitionId,
        string? extraData,
        CancellationToken ct
    );

    /// <summary>
    /// Grants several copies of one definition in a single commit.
    /// </summary>
    /// <remarks>
    /// Callers granting <c>n</c> copies used to call the single-copy grant <c>n</c> times, and every
    /// one of those calls committed on its own. A failure on the third of five left two copies in the
    /// inventory while the compensated scope around the loop refunded all five — the player kept two
    /// pieces of furniture for nothing. One commit removes the partial state rather than compensating
    /// for it.
    /// </remarks>
    public Task GrantFurnitureDefinitionCopiesAsync(
        int definitionId,
        string? extraData,
        int copies,
        CancellationToken ct
    );

    /// <summary>
    /// Grants several copies as a named step of a named operation, once, however many times it is
    /// asked for.
    /// </summary>
    /// <remarks>
    /// The receipt is inserted in the same transaction as the furniture rows, which is what makes a
    /// cross-grain delivery safe to retry. Writing the receipt on the calling side instead leaves no
    /// safe order — receipt first loses the delivery to a crash, delivery first hands out the goods
    /// twice on the retry.
    /// </remarks>
    public Task GrantFurnitureDefinitionCopiesAsync(
        int definitionId,
        string? extraData,
        int copies,
        CommerceOperationId operationId,
        string stepKey,
        CancellationToken ct
    );

    /// <summary>Grants one item whose legacy stuff-data string is baked in at creation -- an
    /// inscribed trophy, a pre-set display. Wraps <paramref name="legacyData"/> in the extra-data
    /// blob shape the stuff-data factory reads back.</summary>
    public Task GrantFurnitureWithLegacyStuffDataAsync(
        int definitionId,
        string legacyData,
        CancellationToken ct
    );
}
