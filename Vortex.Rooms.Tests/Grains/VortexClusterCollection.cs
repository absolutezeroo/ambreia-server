using Xunit;

namespace Vortex.Rooms.Tests.Grains;

/// <summary>
/// Serialises every cluster test into one collection so the silo is deployed once rather than per
/// class, and so two fixtures never race to stand up a silo on the same ports.
/// </summary>
[CollectionDefinition(Name)]
public sealed class VortexClusterCollection : ICollectionFixture<VortexClusterFixture>
{
    public const string Name = "vortex-cluster";
}
