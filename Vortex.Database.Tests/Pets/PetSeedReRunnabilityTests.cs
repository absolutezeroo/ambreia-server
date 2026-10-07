using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Vortex.Database.Seeds;
using Xunit;

namespace Vortex.Database.Tests.Pets;

/// <summary>
/// The pet reference seeds have to survive meeting a database that is not empty.
/// </summary>
/// <remarks>
/// <para>
/// <c>SeedScripts</c> says so in its own summary — "the scripts use <c>INSERT IGNORE</c> and are
/// therefore safe to run against a database that already holds some of the rows" — and
/// <c>pet_palettes.sql</c> did not. It was a plain INSERT, written against the fresh install where
/// the table really is empty. The live hotel had been seeded by hand, so the first deployment
/// carrying that migration died on the unique index over
/// <c>(pet_type, breed_index, rare)</c>, the healthcheck never answered, and the release rolled
/// back.
/// </para>
/// <para>
/// These three are the scripts the pet migrations apply. A migration runs once per database, so
/// nothing re-runs them and no test can catch this by running them twice — what can be checked is
/// the property that makes them safe: every insert either ignores a collision, updates on one, or
/// follows a delete of the table it is about to fill.
/// </para>
/// </remarks>
public sealed class PetSeedReRunnabilityTests
{
    [Theory]
    [InlineData("pet_palettes.sql")]
    [InlineData("pet_levels.sql")]
    public void EveryInsertSurvivesATableThatIsAlreadyPopulated(string script)
    {
        string sql = SeedScripts.Read(script);

        string[] cleared =
        [
            .. Regex
                .Matches(
                    sql,
                    @"(?:DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?)\s+`?(\w+)`?",
                    RegexOptions.IgnoreCase
                )
                .Select(m => m.Groups[1].Value.ToUpperInvariant()),
        ];

        foreach (
            Match insert in Regex.Matches(
                sql,
                @"INSERT\s+(IGNORE\s+)?INTO\s+`?(\w+)`?",
                RegexOptions.IgnoreCase
            )
        )
        {
            if (insert.Groups[1].Success)
            {
                continue;
            }

            string table = insert.Groups[2].Value;
            string statement = StatementAt(sql, insert.Index);

            bool guarded =
                statement.Contains("ON DUPLICATE KEY", System.StringComparison.OrdinalIgnoreCase)
                || cleared.Contains(table.ToUpperInvariant());

            guarded
                .Should()
                .BeTrue(
                    $"{script} inserts into `{table}` with no IGNORE, no upsert and no prior DELETE — "
                        + "it can only be applied to an empty table, and the hotel it meets may not have one"
                );
        }
    }

    /// <summary>The statement starting at <paramref name="start"/>, up to its terminating semicolon.</summary>
    private static string StatementAt(string sql, int start)
    {
        int end = sql.IndexOf(';', start);

        return end < 0 ? sql[start..] : sql[start..end];
    }
}
