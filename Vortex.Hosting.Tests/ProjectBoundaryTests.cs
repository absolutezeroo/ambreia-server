using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Vortex.Hosting.Tests.Architecture;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// AGENTS.md forbids packet handlers from touching database contexts or
/// repositories directly — handlers orchestrate, grains own persistence. That rule was documented
/// but not enforced: <c>Vortex.PacketHandlers.csproj</c> still referenced <c>Vortex.Database</c>, so
/// the first handler to add a <c>using Vortex.Database.Context;</c> would have compiled cleanly and
/// the boundary would have been lost to a code review nobody ran.
/// <para>
/// The reference is gone; this test makes sure it stays gone.
/// </para>
/// </summary>
public sealed class ProjectBoundaryTests
{
    [Fact]
    public void PacketHandlers_DoNotReferenceTheDatabaseProject()
    {
        string csproj = File.ReadAllText(
            Path.Combine(
                RepositoryPaths.Root(),
                "Vortex.PacketHandlers",
                "Vortex.PacketHandlers.csproj"
            )
        );

        csproj
            .Should()
            .NotContain(
                "Vortex.Database\\Vortex.Database.csproj",
                "handlers must reach persistence through grains; the compiler is what keeps that "
                    + "boundary honest"
            );
    }

    [Fact]
    public void PacketHandlers_ContainNoDatabaseUsings()
    {
        string handlers = Path.Combine(RepositoryPaths.Root(), "Vortex.PacketHandlers");

        string[] offenders = Directory
            .EnumerateFiles(handlers, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                File.ReadAllText(f).Contains("using Vortex.Database", StringComparison.Ordinal)
            )
            .ToArray();

        offenders.Should().BeEmpty();
    }
}
