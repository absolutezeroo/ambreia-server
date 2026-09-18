using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

public sealed record CreateNavigatorQuickLinkRequest(
    int ContextId,
    string SearchCode,
    string Filter,
    string Localization,
    int QueryType,
    int OrderNum,
    bool ForceClosed,
    string Reason
) : IReasonedRequest;
