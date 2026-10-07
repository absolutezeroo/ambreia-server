using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Vortex.Hosting.Tests.Architecture;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Kinds;
using Xunit;

namespace Vortex.Hosting.Tests.Rpg;

/// <summary>
/// schemas/content/&lt;kind&gt;.schema.json give an editor autocompletion on the files of content/
/// (spec Contenu §4.2); this keeps them equal to the records. UPDATE_CONTENT_SCHEMAS=1 rewrites them.
/// </summary>
public sealed class ContentSchemaTests
{
    public static TheoryData<string> Kinds() => ["class", "specialization", "progression", "item"];

    private static readonly Dictionary<string, IContentKind> ByName = new()
    {
        ["class"] = new ClassKind(),
        ["specialization"] = new SpecializationKind(),
        ["progression"] = new ProgressionKind(),
        ["item"] = new ItemKind(),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheCommittedSchema_MatchesTheRecord(string kind)
    {
        string exported = Export(ByName[kind].DefinitionType);
        string path = Path.Combine(
            RepositoryPaths.Root(),
            "schemas",
            "content",
            $"{kind}.schema.json"
        );

        if (Environment.GetEnvironmentVariable("UPDATE_CONTENT_SCHEMAS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exported + "\n");

            return;
        }

        File.Exists(path).Should().BeTrue("run UPDATE_CONTENT_SCHEMAS=1 to write it");
        File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Should().Be(exported);
    }

    [Fact]
    public void AListOfIds_IsAnArrayOfText() =>
        Export(ByName["item"].DefinitionType)
            .Should()
            .Contain("\"allowedClasses\": {\n      \"type\": \"array\"");

    [Fact]
    public void TheSchemaLine_IsAllowed() =>
        Export(ByName["class"].DefinitionType).Should().Contain("\"$schema\"");

    private static string Export(Type definition) =>
        JsonSchemaExporter
            .GetJsonSchemaAsNode(
                ContentJson.Options,
                definition,
                new JsonSchemaExporterOptions
                {
                    TreatNullObliviousAsNonNullable = true,
                    TransformSchemaNode = Transform,
                }
            )
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .ReplaceLineEndings("\n");

    private static JsonNode Transform(JsonSchemaExporterContext ctx, JsonNode schema)
    {
        // StrictStringListConverter is custom: the exporter only sees "anything" without this.
        if (ctx.TypeInfo.Type == typeof(List<string>))
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
            };
        }

        // The root admits the editor's "$schema" line, which ContentParser removes before reading.
        if (ctx.Path.IsEmpty && schema["properties"] is JsonObject properties)
        {
            properties.Insert(0, "$schema", new JsonObject { ["type"] = "string" });
        }

        return schema;
    }
}
