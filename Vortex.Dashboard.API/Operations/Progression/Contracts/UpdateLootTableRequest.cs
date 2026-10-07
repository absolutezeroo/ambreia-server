using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record UpdateLootTableRequest(
    int PoolId,
    string Code,
    string Name,
    string Variants,
    string Notes,
    bool Enabled,
    string Reason
) : IReasonedRequest;
