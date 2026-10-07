using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Primitives.Loot.Admin;

/// <summary>
/// Outcome of a prize pool admin write. Mirrors the mystery box admin result: the service is a plain
/// in-process singleton, not a grain, so no Orleans attributes.
/// </summary>
public sealed record LootAdminResult(bool Success, int? Id, string? ErrorCode)
{
    public static LootAdminResult Ok(int id) => new(true, id, null);

    public static LootAdminResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// Create/update spec for a pool. <paramref name="Variants"/> is the comma-separated list of
/// variants entries may be restricted to; empty leaves the pool free-form.
/// </summary>
public sealed record LootTableSpec(
    string Code,
    string Name,
    string Variants,
    string Notes,
    bool Enabled
);

/// <summary>
/// Create/update spec for a pool entry. <paramref name="Variant"/> empty means the entry can drop
/// from any variant; <paramref name="ExtraParam"/> carries the effect spec
/// (<c>effectId[:seconds[:subType]]</c>) or the club month count (<c>months[:vip]</c>).
/// </summary>
public sealed record LootEntrySpec(
    string PoolCode,
    string Variant,
    ProductType ProductType,
    int FurnitureDefinitionId,
    string ExtraParam,
    int Weight,
    bool Enabled
);

/// <summary>
/// Create/update spec for a furniture binding. <paramref name="HitsRequired"/> is how many hits the
/// furniture takes before it pays out; one is a single-click reward box.
/// </summary>
public sealed record LootBindingSpec(
    int FurnitureDefinitionId,
    string PoolCode,
    int HitsRequired,
    bool Enabled
);
