using System.Collections.Generic;

namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// The readiness probe's answer, at 200 when the hotel is healthy or degraded and 503 when the
/// database is unreachable.
/// </summary>
public sealed record HealthResponse(
    string Status,
    string Database,
    IReadOnlyCollection<string> DegradedServices
);
