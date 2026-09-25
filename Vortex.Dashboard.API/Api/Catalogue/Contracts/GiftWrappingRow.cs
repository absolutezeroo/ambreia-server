namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// One gift-dialog choice.
/// </summary>
/// <param name="Kind"><c>Parcel</c>, <c>Box</c> or <c>Ribbon</c>.</param>
/// <param name="FurnitureIconUrl">Parcels only: the furniture's icon, so the row shows the parcel.</param>
/// <param name="SpriteId">
/// Parcels only: the stuff type the client is sent, or null when the name resolves to no furniture —
/// such a row is skipped by the live list, which is the usual reason a freshly added parcel "does not
/// show up".
/// </param>
public sealed record GiftWrappingRow(
    int Id,
    string Kind,
    string? FurnitureName,
    string? FurnitureIconUrl,
    int? SpriteId,
    int? ArtIndex,
    bool IsDefault,
    int SortOrder
);
