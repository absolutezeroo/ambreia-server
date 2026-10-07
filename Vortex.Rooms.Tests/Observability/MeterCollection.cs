using Xunit;

namespace Vortex.Rooms.Tests.Observability;

/// <summary>
/// Serializes the test classes that observe the process-wide "Vortex" meter.
///
/// <see cref="Vortex.Observability.Metrics.RoomPerformanceAggregator"/> listens by meter *name* on
/// purpose — in production it has to aggregate every room in the silo — so it cannot be isolated the
/// way a test-owned recorder can. Run beside another class that emits room ticks it simply counts
/// them too, and an assertion on "avatars took 75% of the tick" reads 66% instead. xunit runs classes
/// in parallel by default, so which test failed moved around and it looked like a phantom.
/// </summary>
[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class MeterCollection
{
    public const string NAME = "vortex-meter";
}
