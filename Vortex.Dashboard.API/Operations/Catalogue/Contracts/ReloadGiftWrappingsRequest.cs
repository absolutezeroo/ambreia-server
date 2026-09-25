using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Catalogue.Contracts;

public sealed record ReloadGiftWrappingsRequest(string Reason) : IReasonedRequest;
