using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests;

/// <summary>
///     A composer with no registered serializer does not fail, log an error, or throw — PackageEncoder
///     writes zero bytes and the packet simply never reaches the client, leaving only a
///     <c>PacketDropped("serializer_not_found")</c> counter behind. That silence is why this bug class
///     has shipped repeatedly: the room-settings dialog, the guild packets, the entire staff mod tool,
///     and the CFH close notification all reached production dead this way.
///     <para>
///     So the rule is enforced here rather than trusted: anything the server code constructs must be
///     serializable. The test reads the source tree because the fault is a missing line in a map file,
///     which no amount of reflection over the compiled revision can notice — an unregistered composer
///     is indistinguishable from one nobody uses.
///     </para>
///     <para>
///     Writing a serializer is not enough. Wire it into the matching <c>Maps/*.cs</c> too.
///     </para>
/// </summary>
public sealed class EmittedComposerRegistrationTests
{
    /// <summary>Projects whose code runs on the server and may send packets. Test projects are
    /// excluded: a test may legitimately construct a composer it never sends.</summary>
    private static readonly string[] EmittingProjects =
    [
        "Vortex.Catalog",
        "Vortex.Inventory",
        "Vortex.Marketplace",
        "Vortex.Navigator",
        "Vortex.PacketHandlers",
        "Vortex.Players",
        "Vortex.Rooms",
        "Vortex.WebApi",
    ];

    private static readonly Regex ComposerConstruction = new(
        @"\bnew\s+(?<name>\w+Composer)\b",
        RegexOptions.Compiled
    );

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vortex.Cloud.sln")))
        {
            dir = dir.Parent;
        }

        return dir
            ?? throw new InvalidOperationException(
                "Could not locate the repository root (no Vortex.Cloud.sln above "
                    + $"'{AppContext.BaseDirectory}')."
            );
    }

    private static IEnumerable<string> ServerSourceFiles(DirectoryInfo root) =>
        EmittingProjects
            .Select(project => Path.Combine(root.FullName, project))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            );

    [Fact]
    public void EveryComposerTheServerConstructs_HasARegisteredSerializer()
    {
        DirectoryInfo root = RepositoryRoot();

        HashSet<string> registered = Revision
            .Serializers.Keys.Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        SortedDictionary<string, SortedSet<string>> unregistered = new(StringComparer.Ordinal);

        foreach (string path in ServerSourceFiles(root))
        {
            string source = File.ReadAllText(path);

            foreach (Match match in ComposerConstruction.Matches(source))
            {
                string name = match.Groups["name"].Value;

                if (registered.Contains(name))
                {
                    continue;
                }

                if (!unregistered.TryGetValue(name, out SortedSet<string>? sites))
                {
                    sites = new SortedSet<string>(StringComparer.Ordinal);
                    unregistered[name] = sites;
                }

                sites.Add(Path.GetRelativePath(root.FullName, path));
            }
        }

        unregistered
            .Should()
            .BeEmpty(
                "these composers are constructed by server code but have no serializer registered "
                    + "in Revision20260701, so every one of them is silently dropped by "
                    + "PackageEncoder:\n"
                    + string.Join(
                        "\n",
                        unregistered.Select(pair =>
                            $"  {pair.Key} <- {string.Join(", ", pair.Value)}"
                        )
                    )
            );
    }

    /// <summary>
    ///     Guards the guard: if the project layout moves and none of the expected directories exist,
    ///     the test above would pass by scanning nothing at all.
    /// </summary>
    [Fact]
    public void TheScanActuallyReachesTheServerSource()
    {
        DirectoryInfo root = RepositoryRoot();

        List<string> files = [.. ServerSourceFiles(root)];

        files
            .Should()
            .NotBeEmpty("the emitting-project list must still match the repository layout");

        files
            .Count(path => ComposerConstruction.IsMatch(File.ReadAllText(path)))
            .Should()
            .BeGreaterThan(10, "server code is expected to construct composers in many files");
    }
}
