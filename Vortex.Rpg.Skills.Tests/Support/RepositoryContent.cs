using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Tests.Support;

/// <summary>The skills and statuses of content/ as shipped, read from the repository the way the
/// import reads them (the editor's "$schema" line left out).</summary>
internal static class RepositoryContent
{
    public static ContentDefinition[] SkillsAndStatuses()
    {
        string content = Path.Combine(Root(), "content");

        return
        [
            .. Directory
                .GetFiles(Path.Combine(content, "skill"), "*.json")
                .Select(f => (ContentDefinition)ContentJson.Deserialize<SkillDefinition>(Read(f))),
            .. Directory
                .GetFiles(Path.Combine(content, "status"), "*.json")
                .Select(f => (ContentDefinition)ContentJson.Deserialize<StatusDefinition>(Read(f))),
        ];
    }

    private static string Read(string file)
    {
        JsonObject document = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        document.Remove("$schema");

        return document.ToJsonString();
    }

    private static string Root()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vortex.Cloud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
