using System.Collections.Generic;

namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// The liveness probe's answer: always 200 while the process answers, <c>Degraded</c> when an
/// optional service failed to start. It never asks the database — a hotel whose database is away is
/// alive, and restarting it would not bring the database back.
/// </summary>
public sealed record HealthResponse(string Status, IReadOnlyCollection<string> DegradedServices);
