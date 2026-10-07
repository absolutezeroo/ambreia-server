using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record CreateLootTableRequest(
    string Code,
    string Name,
    string Variants,
    string Notes,
    bool Enabled,
    string Reason
) : IReasonedRequest;
