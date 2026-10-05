using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// Orleans serialises a grain's turns. An interleaving attribute opts one method out of that, and is
/// therefore the only place in the codebase where another turn can observe a half-applied state
/// change. Before this, the reasoning for the one method that needs it lived in a comment on
/// <c>IPlayerPresenceGrain</c> — and a superseded revision of the architecture note had already
/// written a rule ("every interleaved method returns an immutable") that would have banned it.
/// <para>
/// So the reasoning moved to <c>Vortex.Hosting.Tests/Architecture/interleaving-manifest.yaml</c>, and this test
/// holds it to the code: every attributed method is listed, every listed method still carries its
/// attribute, and the property that makes the liveness category safe — no <c>await</c> before the
/// mutation completes — is checked in the body rather than asserted in prose.
/// </para>
/// </summary>
public sealed class InterleavingManifestTests
{
    private const string IMMUTABLE_READ = "ImmutableRead";
    private const string LIVENESS = "SynchronousBoundedLivenessOperation";

    private static readonly string[] INTERLEAVING_ATTRIBUTES =
    [
        "AlwaysInterleaveAttribute",
        "ReentrantAttribute",
        "MayInterleaveAttribute",
    ];

    [Fact]
    public void EveryInterleavedMethod_IsListedInTheManifest()
    {
        string[] listed = Manifest().Select(Key).ToArray();
        string[] attributed = AttributedMethods().Select(m => Key(m.Symbol, m.Overload)).ToArray();

        attributed
            .Except(listed)
            .Should()
            .BeEmpty(
                "an interleaved method with no manifest entry is an unreviewed concurrency "
                    + "decision; add it to Vortex.Hosting.Tests/Architecture/interleaving-manifest.yaml with its "
                    + "category, or an ADR if it fits neither"
            );
    }

    [Fact]
    public void EveryManifestEntry_StillCarriesItsAttribute()
    {
        string[] listed = Manifest().Select(Key).ToArray();
        string[] attributed = AttributedMethods().Select(m => Key(m.Symbol, m.Overload)).ToArray();

        listed
            .Except(attributed)
            .Should()
            .BeEmpty(
                "the manifest describes methods that no longer interleave; delete the stale entries "
                    + "so the file keeps meaning what it says"
            );
    }

    [Fact]
    public void EveryEntry_DeclaresALegalCategory()
    {
        foreach (Dictionary<string, string> entry in Manifest())
        {
            entry
                .GetValueOrDefault("category")
                .Should()
                .BeOneOf(
                    [IMMUTABLE_READ, LIVENESS],
                    "{0} is outside the two categories ADR-000 allows; anything else needs its own ADR",
                    Key(entry)
                );
        }
    }

    /// <summary>
    /// The safety property of the liveness category, checked rather than trusted: the whole mutation
    /// happens before the first suspension point, so no other turn can catch it half-applied. A body
    /// that awaits has stopped being safe to interleave, whatever the comment above it says.
    /// </summary>
    [Fact]
    public void LivenessEntries_CompleteTheirMutationBeforeAnyAwait()
    {
        foreach (Dictionary<string, string> entry in Manifest())
        {
            if (entry.GetValueOrDefault("category") != LIVENESS)
            {
                continue;
            }

            string relative = entry.GetValueOrDefault("implementation") ?? string.Empty;
            relative.Should().NotBeEmpty("{0} must name its implementation file", Key(entry));

            string path = Path.Combine(
                RepositoryPaths.Root(),
                relative.Replace('/', Path.DirectorySeparatorChar)
            );
            File.Exists(path)
                .Should()
                .BeTrue("{0} names {1}, which does not exist", Key(entry), relative);

            string method = MethodName(entry);
            string[] bodies = MethodBodies(File.ReadAllText(path), method);

            bodies
                .Should()
                .NotBeEmpty("{0} names {1}, which contains no {2}", Key(entry), relative, method);

            foreach (string body in bodies)
            {
                body.Should()
                    .NotContain(
                        "await ",
                        "{0} is category {1}: the mutation must complete synchronously, so the body "
                            + "may not suspend. The expected shape is `mutate; LogAndForget(...); "
                            + "return Task.CompletedTask;`",
                        Key(entry),
                        LIVENESS
                    );
            }
        }
    }

    private static List<Dictionary<string, string>> Manifest() =>
        ManifestFile.ReadEntries(RepositoryPaths.TestData("interleaving-manifest.yaml"), "entries");

    private static string Key(Dictionary<string, string> entry) =>
        Key(entry.GetValueOrDefault("symbol") ?? "?", entry.GetValueOrDefault("overload") ?? "()");

    private static string Key(string symbol, string overload) => $"{symbol}{overload}";

    private static string MethodName(Dictionary<string, string> entry)
    {
        string symbol = entry.GetValueOrDefault("symbol") ?? string.Empty;
        int lastDot = symbol.LastIndexOf('.');

        return lastDot < 0 ? symbol : symbol[(lastDot + 1)..];
    }

    /// <summary>
    /// Every body of a named method in a source file, brace-matched from its signature. Crude on
    /// purpose: it only has to be right about where a method ends, and a repository that formats
    /// with csharpier gives it well-behaved braces to count.
    /// </summary>
    private static string[] MethodBodies(string source, string methodName)
    {
        List<string> bodies = [];
        int search = 0;

        while (true)
        {
            int at = source.IndexOf($" {methodName}(", search, StringComparison.Ordinal);

            if (at < 0)
            {
                break;
            }

            search = at + methodName.Length;

            int open = source.IndexOf('{', at);
            int semicolon = source.IndexOf(';', at);

            // An expression-bodied or abstract declaration has no block to inspect.
            if (open < 0 || (semicolon >= 0 && semicolon < open))
            {
                continue;
            }

            int depth = 0;

            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;

                    if (depth == 0)
                    {
                        bodies.Add(source[open..(i + 1)]);
                        search = i;

                        break;
                    }
                }
            }
        }

        return [.. bodies];
    }

    internal static IEnumerable<(string Symbol, string Overload)> AttributedMethods()
    {
        foreach (Type type in VortexTypes.All())
        {
            foreach (
                MethodInfo method in type.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly
                )
            )
            {
                if (!HasInterleavingAttribute(method))
                {
                    continue;
                }

                yield return ($"{type.FullName}.{method.Name}", Overload(method));
            }
        }
    }

    private static bool HasInterleavingAttribute(MemberInfo member) =>
        member
            .GetCustomAttributes(inherit: false)
            .Any(a => INTERLEAVING_ATTRIBUTES.Contains(a.GetType().Name, StringComparer.Ordinal));

    private static string Overload(MethodBase method) =>
        $"({string.Join(", ", method.GetParameters().Select(p => TypeName(p.ParameterType)))})";

    private static string TypeName(Type type) =>
        type.IsArray ? $"{TypeName(type.GetElementType()!)}[]" : type.Name;
}
