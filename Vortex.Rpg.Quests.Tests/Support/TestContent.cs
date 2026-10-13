using System.IO;
using System.Text.Json.Nodes;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Quests.Tests.Support;

/// <summary>A monster and an item of the shipped content, for the kinds' lookups.</summary>
internal static class TestContent
{
    public static readonly MonsterDefinition Wolf = Read<MonsterDefinition>(
        "monster/forest_wolf.json"
    );

    public static readonly ItemDefinition Shard = Read<ItemDefinition>("item/amber_shard.json");

    /// <summary>As the import reads a file: the editor's $schema line taken out first.</summary>
    private static T Read<T>(string file)
    {
        JsonObject document = (JsonObject)
            JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "content", file)))!;

        document.Remove("$schema");

        return ContentJson.Deserialize<T>(document.ToJsonString());
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? at = new(System.AppContext.BaseDirectory);

        while (at is not null && !File.Exists(Path.Combine(at.FullName, "Vortex.Cloud.sln")))
        {
            at = at.Parent;
        }

        return at?.FullName
            ?? throw new DirectoryNotFoundException(
                "Vortex.Cloud.sln not found above the test binaries."
            );
    }
}
