using System.Collections.Generic;

namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// The readiness probe's answer: 200 when the hotel can take a player (listeners open, host not
/// draining, database reachable), 503 otherwise with what blocks it.
/// </summary>
public sealed record ReadinessResponse(
    string Status,
    string? Blocker,
    string Database,
    IReadOnlyCollection<string> DegradedServices
);
