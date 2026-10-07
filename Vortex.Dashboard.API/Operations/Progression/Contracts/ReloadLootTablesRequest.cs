using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record ReloadLootTablesRequest(string Reason) : IReasonedRequest;
