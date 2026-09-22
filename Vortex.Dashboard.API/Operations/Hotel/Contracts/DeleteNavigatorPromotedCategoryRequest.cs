using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

public sealed record DeleteNavigatorPromotedCategoryRequest(int PromotedCategoryId, string Reason)
    : IReasonedRequest;
