using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Catalogue.Contracts;

/// <summary>
/// Request bodies for the gift dialog's choices, each carrying a mandatory audited <c>Reason</c>.
/// </summary>
/// <remarks>
/// <c>Kind</c> travels as its name (<c>Parcel</c>, <c>Box</c>, <c>Ribbon</c>) and is parsed at the
/// boundary: a value outside the enum fails the request rather than defaulting to a parcel.
/// </remarks>
public sealed record CreateGiftWrappingRequest(
    string Kind,
    string? FurnitureName,
    int? ArtIndex,
    bool IsDefault,
    int SortOrder,
    string Reason
) : IReasonedRequest;
