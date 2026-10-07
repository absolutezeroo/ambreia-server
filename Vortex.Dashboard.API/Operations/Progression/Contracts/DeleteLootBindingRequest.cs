using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record DeleteLootBindingRequest(int BindingId, string Reason) : IReasonedRequest;
