using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

public sealed record CreateNavigatorPromotedCategoryRequest(
    string Code,
    string SearchCode,
    string Filter,
    int OrderNum,
    bool Visible,
    string Reason
) : IReasonedRequest;
