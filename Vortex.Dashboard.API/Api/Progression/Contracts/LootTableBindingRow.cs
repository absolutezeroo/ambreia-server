namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One piece of furniture bound to a pool.
/// </summary>
/// <param name="HitsRequired">How many times it must be used before it pays out.</param>
/// <param name="FurnitureLogic">What the furniture actually does. A binding on a definition whose
/// logic never fires is a pool nothing draws from, which the weights alone cannot show.</param>
public sealed record LootTableBindingRow(
    int Id,
    int FurnitureDefinitionId,
    string Pool,
    int HitsRequired,
    bool Enabled,
    string? FurnitureName,
    string? FurnitureLogic,
    string? FurnitureIconUrl
);
