using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// What players have actually caught. Read-only: a record is something that happened, not something
/// an operator sets.
/// </summary>
/// <param name="Anglers">How many players have ever fished.</param>
public sealed record FishingActivity(IReadOnlyList<FishingRecordRow> Records, int Anglers);
