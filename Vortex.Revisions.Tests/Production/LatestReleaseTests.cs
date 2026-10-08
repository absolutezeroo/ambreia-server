using FluentAssertions;
using Vortex.Revisions;
using Xunit;

namespace Vortex.Revisions.Tests.Production;

public sealed class LatestReleaseTests
{
    [Fact]
    public void TheLatestRevision_IsTheLastByName()
    {
        ReleaseRevision
            .LatestReleaseName(
                new[]
                {
                    "Releases/AMBREIA-202610081530.json",
                    "Vortex.Revisions.Something.txt",
                    "Releases/AMBREIA-202612010900.json",
                    "Releases/AMBREIA-202611150000.json",
                }
            )
            .Should()
            .Be("Releases/AMBREIA-202612010900.json");
    }

    [Fact]
    public void NoRelease_IsNull()
    {
        ReleaseRevision
            .LatestReleaseName(new[] { "Vortex.Revisions.Something.txt" })
            .Should()
            .BeNull();
    }
}
