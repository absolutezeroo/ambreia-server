namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One thing a pool can hand out, and how likely it is.
/// </summary>
/// <param name="Variant">Null means the entry competes in every variant of its pool.</param>
/// <param name="FurnitureName">Null when the entry is not furniture, or when the definition is
/// gone. The icon follows it, because an operator recognises a sofa, not definition id 4312.</param>
public sealed record LootTableEntryRow(
    int Id,
    int PoolId,
    string Pool,
    string? Variant,
    string ProductType,
    int? FurnitureDefinitionId,
    string? ExtraParam,
    int Weight,
    bool Enabled,
    string? FurnitureName,
    string? FurnitureIconUrl
);
