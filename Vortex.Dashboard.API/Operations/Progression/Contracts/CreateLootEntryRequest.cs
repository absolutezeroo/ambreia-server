using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record CreateLootEntryRequest(
    string PoolCode,
    string Variant,
    string ProductType,
    int FurnitureDefinitionId,
    string ExtraParam,
    int Weight,
    bool Enabled,
    string Reason
) : IReasonedRequest;
