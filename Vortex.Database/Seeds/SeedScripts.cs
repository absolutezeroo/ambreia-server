using System;
using System.IO;
using System.Reflection;

namespace Vortex.Database.Seeds;

/// <summary>
/// Loads the reference-data seed scripts embedded from <c>Vortex.Database/Seeds/*.sql</c>, so a
/// migration can apply one with <c>migrationBuilder.Sql(SeedScripts.Read("x.sql"))</c>. Each runs
/// once, inside <c>InitialCreate</c>, against a fresh database; the furniture-dependent ones are
/// replayed by <c>scripts/import-reference-data.ps1</c> and are written to be re-runnable.
/// </summary>
public static class SeedScripts
{
    private const string ResourcePrefix = "Vortex.Database.Seeds.";

    public static string Read(string fileName)
    {
        Assembly assembly = typeof(SeedScripts).Assembly;
        string resourceName = ResourcePrefix + fileName;

        using Stream stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Seed script '{fileName}' is not embedded in {assembly.GetName().Name}. "
                    + "Check the EmbeddedResource glob in Vortex.Database.csproj."
            );

        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }
}
