using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests.Architecture;

/// <summary>
/// The deployment thesis is single-silo, and a second silo is refused at startup. That refusal is
/// the guard; the debt behind it is the problem — reference caches whose reload reaches one process,
/// and aggregators that report one node. None of them fail loudly when they are wrong, which is why
/// nobody ever reports them.
/// <para>
/// This test does not forbid adding another one. It requires that adding one is a decision somebody
/// wrote down, so the bill for lifting the thesis stays known.
/// </para>
/// </summary>
public sealed class SingleSiloInventoryTests
{
    [Fact]
    public void EveryReferenceDataProvider_IsInTheInventory()
    {
        string[] implementations =
        [
            .. VortexTypes
                .All()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && t.GetInterfaces().Any(i => i.Name == "IReferenceDataProvider")
                )
                .Select(t => t.Name),
        ];

        implementations.Should().NotBeEmpty("the providers are what is being inventoried");

        implementations
            .Except(Inventory("reference_data_providers"))
            .Should()
            .BeEmpty(
                "a new reference cache is new multi-silo debt: its ReloadAsync reaches the calling "
                    + "process only. Add it to Vortex.Hosting.Tests/Architecture/single-silo-inventory.yaml"
            );
    }

    [Fact]
    public void EveryAggregator_IsInTheInventory()
    {
        string[] aggregators =
        [
            .. VortexTypes
                .All()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && t.Name.EndsWith("Aggregator", StringComparison.Ordinal)
                )
                .Select(t => t.Name),
        ];

        aggregators
            .Except(Inventory("aggregators"))
            .Should()
            .BeEmpty(
                "an aggregator sums the silo it runs on; on a second silo the dashboard reports "
                    + "whichever one it reached. Add it to the inventory"
            );
    }

    /// <summary>Reads a plain `- value` list under the given key.</summary>
    private static IReadOnlyCollection<string> Inventory(string key)
    {
        List<string> values = [];
        bool inList = false;

        foreach (
            string raw in File.ReadAllLines(RepositoryPaths.TestData("single-silo-inventory.yaml"))
        )
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]) && !trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                inList = trimmed.StartsWith($"{key}:", StringComparison.Ordinal);

                continue;
            }

            if (inList && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                values.Add(trimmed[2..].Trim());
            }
        }

        return values;
    }
}
