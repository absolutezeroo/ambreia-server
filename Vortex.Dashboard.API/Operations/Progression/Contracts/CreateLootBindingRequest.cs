using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record CreateLootBindingRequest(
    int FurnitureDefinitionId,
    string PoolCode,
    int HitsRequired,
    bool Enabled,
    string Reason
) : IReasonedRequest;
