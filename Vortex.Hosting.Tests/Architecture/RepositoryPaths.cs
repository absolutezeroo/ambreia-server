using System;
using System.IO;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// Where the repository is, from inside the test binaries. The architecture guards read source and
/// documentation files rather than compiled metadata, because half of what they assert (a manifest
/// entry, a state file key) has no compiled form at all.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>Walks up from the test binaries until the solution file is found.</summary>
    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Vortex.Cloud.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    public static string TestData(params string[] segments) =>
        Path.Combine([Root(), "Vortex.Hosting.Tests", "Architecture", .. segments]);
}
