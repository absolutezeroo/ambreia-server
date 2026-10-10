using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// The image is built from the repository, and nothing on a developer's machine runs it: a project
/// deleted from the tree stays in the Dockerfile until the first deployment fails. This test is the
/// build that nobody runs locally — every project the Dockerfile copies, restores or publishes must
/// exist.
/// </summary>
public sealed partial class DockerfileTests
{
    [Fact]
    public void EveryProjectTheDockerfileReferences_ExistsInTheRepository()
    {
        string root = RepositoryPaths.Root();
        string dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        string[] referenced =
        [
            .. ProjectReference()
                .Matches(dockerfile)
                .Select(m => m.Groups["project"].Value)
                .Distinct(),
        ];

        referenced.Should().NotBeEmpty("the Dockerfile publishes at least the host");

        referenced
            .Where(project => !Directory.Exists(Path.Combine(root, project)))
            .Should()
            .BeEmpty("a project the Dockerfile references must exist, or `docker build` fails");
    }

    /// <summary>A <c>Vortex.Something</c> path segment, wherever the Dockerfile names one.</summary>
    [GeneratedRegex(@"(?<project>Vortex\.[A-Za-z.]+?)(?:/|\\|\.csproj)")]
    private static partial Regex ProjectReference();
}
