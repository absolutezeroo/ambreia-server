using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

public sealed record DeleteLootEntryRequest(int EntryId, string Reason) : IReasonedRequest;
