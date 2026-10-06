using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Vortex.Database.Context;
using Vortex.Database.Seeds;
using Xunit;

namespace Vortex.Database.Tests.Migrations;

/// <summary>
/// The seeds a fresh database is built from, against the one migration that applies them.
/// </summary>
/// <remarks>
/// <para>
/// A seed is an embedded resource that only runs when a migration calls
/// <c>SeedScripts.Read</c> on it. One left behind by a removed migration builds, embeds and does
/// nothing; one that names a table the model no longer has embeds just as quietly and only fails
/// on the first <c>database update</c>, which no other test runs.
/// </para>
/// <para>
/// No MySQL here: the migration's operations are built in memory, and the tables a seed names are
/// read off its text and checked against the EF model.
/// </para>
/// </remarks>
public sealed class SeedCatalogTests
{
    private const string ResourcePrefix = "Vortex.Database.Seeds.";

    /// <summary>The tables of the features removed in M0 and M0 bis. A seed naming one targets
    /// nothing.</summary>
    private static readonly string[] RemovedTables =
    [
        "habbicon",
        "reward_track",
        "poll",
        "mystery_box",
        "nft",
        "wired",
        "builders_club",
        "voucher",
        "targeted_offer",
        "ltd",
        "songs",
        "achievement_resolution",
        "account_level",
        "quiz",
        "furniture_teleport_link",
    ];

    private static readonly Assembly DatabaseAssembly = typeof(SeedScripts).Assembly;

    private static IReadOnlyList<string> Seeds =>
        [
            .. DatabaseAssembly
                .GetManifestResourceNames()
                .Where(n =>
                    n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    && n.EndsWith(".sql", StringComparison.Ordinal)
                )
                .Select(n => n[ResourcePrefix.Length..]),
        ];

    private static IReadOnlyList<Type> MigrationTypes =>
        [
            .. DatabaseAssembly
                .GetTypes()
                .Where(t =>
                    typeof(Migration).IsAssignableFrom(t)
                    && t.GetCustomAttribute<MigrationAttribute>() is not null
                ),
        ];

    [Fact]
    public void TheHistoryIsOneInitialCreate() =>
        MigrationTypes
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()!.Id)
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("_InitialCreate");

    [Fact]
    public void EverySeedIsAppliedByInitialCreate()
    {
        Type initialCreate = MigrationTypes.Single(t =>
            t.GetCustomAttribute<MigrationAttribute>()!
                .Id.EndsWith("_InitialCreate", StringComparison.Ordinal)
        );

        Migration migration = (Migration)Activator.CreateInstance(initialCreate)!;

        HashSet<string> applied =
        [
            .. migration.UpOperations.OfType<SqlOperation>().Select(o => o.Sql),
        ];

        Seeds.Should().NotBeEmpty();
        Seeds
            .Where(seed => !applied.Contains(SeedScripts.Read(seed)))
            .Should()
            .BeEmpty("an embedded seed nothing applies never reaches a database");
    }

    [Fact]
    public void NoSeedNamesARemovedTable()
    {
        foreach (string seed in Seeds)
        {
            TablesNamedIn(SeedScripts.Read(seed))
                .Where(table => RemovedTables.Any(removed => table.Contains(removed)))
                .Should()
                .BeEmpty("'{0}' must not target a table of a removed feature", seed);
        }
    }

    [Fact]
    public void EveryTableASeedNamesIsInTheModel()
    {
        using VortexDbContext db = new(
            new DbContextOptionsBuilder<VortexDbContext>().UseSqlite("Filename=:memory:").Options
        );

        HashSet<string> modelled =
        [
            .. db.GetService<IDesignTimeModel>()
                .Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .OfType<string>(),
        ];

        foreach (string seed in Seeds)
        {
            string sql = SeedScripts.Read(seed);
            HashSet<string> temporary =
            [
                .. Regex
                    .Matches(
                        sql,
                        @"CREATE\s+TEMPORARY\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?\s+`?(\w+)`?",
                        RegexOptions.IgnoreCase
                    )
                    .Select(m => m.Groups[1].Value.ToLowerInvariant()),
            ];

            TablesNamedIn(sql)
                .Where(table => !modelled.Contains(table) && !temporary.Contains(table))
                .Should()
                .BeEmpty("'{0}' names tables the model does not have", seed);
        }
    }

    /// <summary>
    /// The tables a script reads or writes, with comments and string literals blanked first so a
    /// sentence like "taken from furnidata" or a name like 'clothing_nft...' is not read as one.
    /// </summary>
    private static IEnumerable<string> TablesNamedIn(string sql)
    {
        string code = Regex.Replace(sql, @"--[^\n]*|'(?:[^']|'')*'", " ");

        return Regex
            .Matches(
                code,
                @"\b(?:INTO|(?<!KEY\s+)UPDATE(?:\s+IGNORE)?|FROM|JOIN|TABLE(?:\s+IF(?:\s+NOT)?\s+EXISTS)?)\s+`?(\w+)`?",
                RegexOptions.IgnoreCase
            )
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct();
    }
}
