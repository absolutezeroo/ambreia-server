using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>One page of the pet species picker. Answers in one go: there are a few dozen.</summary>
public sealed record PetDirectoryPage(
    int Count,
    int Total,
    int Offset,
    bool HasMore,
    IReadOnlyList<PetDirectoryRow> Items
);
