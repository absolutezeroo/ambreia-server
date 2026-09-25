using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Catalogue.Contracts;

public sealed record UpdateGiftWrappingRequest(
    int WrappingId,
    string Kind,
    string? FurnitureName,
    int? ArtIndex,
    bool IsDefault,
    int SortOrder,
    string Reason
) : IReasonedRequest;
