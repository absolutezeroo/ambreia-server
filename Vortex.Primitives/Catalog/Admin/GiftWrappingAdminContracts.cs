using Vortex.Primitives.Catalog.Enums;

namespace Vortex.Primitives.Catalog.Admin;

/// <summary>
/// One gift-dialog choice as an operator edits it. A parcel names its furniture; a box or a ribbon
/// gives its art index. The field the kind does not use is ignored. Not to be confused with
/// <see cref="Snapshots.GiftWrappingSpec"/>, which is how one purchase was wrapped.
/// </summary>
public sealed record GiftWrappingEntrySpec(
    GiftWrappingKind Kind,
    string? FurnitureName,
    int? ArtIndex,
    bool IsDefault,
    int SortOrder
);

/// <summary>The outcome of one gift wrapping admin write.</summary>
public sealed record GiftWrappingAdminResult
{
    public required bool Success { get; init; }

    /// <summary>A machine-readable reason, empty on success.</summary>
    public required string Error { get; init; }

    /// <summary>The row that was written, when there is one.</summary>
    public int WrappingId { get; init; }

    public static GiftWrappingAdminResult Ok(int wrappingId) =>
        new()
        {
            Success = true,
            Error = string.Empty,
            WrappingId = wrappingId,
        };

    public static GiftWrappingAdminResult Fail(string error) =>
        new() { Success = false, Error = error };
}
