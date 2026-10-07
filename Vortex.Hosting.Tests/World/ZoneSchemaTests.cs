using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Schema;
using FluentAssertions;
using Vortex.Hosting.Tests.Architecture;
using Vortex.Primitives.World;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.World;

/// <summary>
/// The editor generates its TypeScript types from schemas/zone.schema.json; this keeps that file
/// equal to the C# records. UPDATE_ZONE_SCHEMA=1 rewrites it.
/// </summary>
public sealed class ZoneSchemaTests
{
    [Fact]
    public void TheCommittedSchema_MatchesTheRecords()
    {
        string exported = JsonSchemaExporter
            .GetJsonSchemaAsNode(ZoneJson.Options, typeof(ZoneDocument))
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .ReplaceLineEndings("\n");
        string path = Path.Combine(RepositoryPaths.Root(), "schemas", "zone.schema.json");

        if (Environment.GetEnvironmentVariable("UPDATE_ZONE_SCHEMA") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exported + "\n");

            return;
        }

        File.Exists(path).Should().BeTrue("run UPDATE_ZONE_SCHEMA=1 to write it");
        File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Should().Be(exported);
    }

    [Fact]
    public void ADocumentWithEveryLayer_SurvivesARoundTrip()
    {
        string json = ZoneJson.Serialize(ZoneSamples.EveryLayer());

        ZoneJson.Serialize(ZoneJson.Deserialize(json)).Should().Be(json);
        json.Should().Contain("\"kind\":\"PlayerLevel\"").And.Contain("\"type\":\"World\"");
    }

    [Fact]
    public void AnUnknownConditionKind_DoesNotParse()
    {
        string json = ZoneJson
            .Serialize(ZoneSamples.EveryLayer())
            .Replace("\"kind\":\"PlayerLevel\"", "\"kind\":\"Telepathy\"");

        Action parse = () => ZoneJson.Deserialize(json);

        parse.Should().Throw<JsonException>();
    }
}
