using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// Every way a player can change the furniture in a room goes through <c>RoomActionModule</c>, and
/// every one of them asks <c>SecurityModule</c> first.
/// </summary>
/// <remarks>
/// <para>
/// That used to hold by convention only, and one entry point (since removed with its feature)
/// broke it: it checked that the item existed and nothing else, so any visitor could rewrite the
/// item's configuration and be answered with a success. It was not visible in a build, a test, or
/// a grep; it was visible by reading the method next to the one above it.
/// </para>
/// <para>
/// A facade class would not have caught it — a method added to a facade skips a check exactly as
/// easily as a method added to a module. What catches it is naming the convention and failing the
/// build when a new entry point does not follow it, which is the same bargain
/// <see cref="RoomGrainConcurrencyTests" /> makes for the concurrency model.
/// </para>
/// </remarks>
public sealed class FurnitureMutationBoundaryTests
{
    /// <summary>
    /// Entry points that legitimately reach the room without a permission check, and why. Anything
    /// not on this list must consult the security module.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["ClickItemByIdAsync"] =
            "a click reaches OnClickAsync, which no logic in the hotel overrides -- "
            + "ClickIsStillInert below is what keeps that true",
    };

    [Fact]
    public void EveryFurnitureMutationEntryPoint_ConsultsTheSecurityModule()
    {
        List<string> unguarded = [];

        foreach ((string name, string body) in PublicMethodsOf("RoomActionModule"))
        {
            if (!Exempt.ContainsKey(name) && !body.Contains("SecurityModule"))
            {
                unguarded.Add(name);
            }
        }

        unguarded
            .Should()
            .BeEmpty(
                "RoomActionModule is the furniture boundary: its methods are reachable from a packet "
                    + "handler, and a handler is not a security boundary. An entry point that needs no "
                    + "check belongs in Exempt with the reason, not without one"
            );
    }

    /// <summary>
    /// Keeps the one exemption honest. The click path is unguarded because it does nothing; the day
    /// a logic overrides <c>OnClickAsync</c> it starts doing something, for anybody standing in the
    /// room, and this fails rather than shipping.
    /// </summary>
    [Fact]
    public void ClickIsStillInert()
    {
        IEnumerable<string> overrides = SourceFiles(
                Path.Combine(RepositoryPaths.Root(), "Vortex.Rooms", "Object", "Logic")
            )
            .Where(file => File.ReadAllText(file).Contains("override", StringComparison.Ordinal))
            .Where(file =>
                Regex.IsMatch(
                    File.ReadAllText(file),
                    @"override\s+\w[\w<>,\s\.]*\s+OnClickAsync\s*\("
                )
            );

        overrides
            .Should()
            .BeEmpty(
                "the click entry point is exempt from the permission check only because it reaches a "
                    + "hook nothing implements. A logic overriding OnClickAsync makes the click do "
                    + "something, so the entry point needs a policy before that ships"
            );
    }

    /// <summary>
    /// Public methods of a module, as (name, body) pairs. Crude on purpose: it reads the source the
    /// way a reviewer does, which is what this is standing in for.
    /// </summary>
    private static IEnumerable<(string Name, string Body)> PublicMethodsOf(string module)
    {
        string directory = Path.Combine(
            RepositoryPaths.Root(),
            "Vortex.Rooms",
            "Grains",
            "Modules"
        );

        foreach (
            string file in Directory
                .EnumerateFiles(directory, $"{module}*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
        )
        {
            string source = File.ReadAllText(file);

            MatchCollection starts = Regex.Matches(
                source,
                @"^\s{4}public\s+(?:async\s+)?[\w<>,\s\.\?]+?\s(?<name>\w+)\s*\(",
                RegexOptions.Multiline
            );

            for (int i = 0; i < starts.Count; i++)
            {
                int from = starts[i].Index;
                int to = i + 1 < starts.Count ? starts[i + 1].Index : source.Length;

                yield return (starts[i].Groups["name"].Value, source[from..to]);
            }
        }
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            );
}
