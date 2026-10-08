using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Vortex.Revisions;

/// <summary>
/// One production revision's header ids, as <c>scripts/revision-new.mjs</c> drew them:
/// <c>{"revision": "AMBREIA-…", "events": {name: id}, "composers": {name: id}}</c>.
/// Spec: vortex-rpg-client docs/superpowers/specs/2026-10-08-random-header-revisions-design.md.
/// </summary>
public sealed record ReleaseTable(
    string Revision,
    IReadOnlyDictionary<string, int> Events,
    IReadOnlyDictionary<string, int> Composers
)
{
    /// <summary>
    /// ClientHello and DisconnectReason keep this id in every revision: the first is read before the
    /// server knows the client's table, the second must reach a client on any table.
    /// </summary>
    public const int FixedHeader = 4000;

    public static ReleaseTable Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        return new ReleaseTable(
            root.GetProperty("revision").GetString()
                ?? throw new InvalidDataException("release table: \"revision\" is null"),
            Read(root, "events"),
            Read(root, "composers")
        );
    }

    private static Dictionary<string, int> Read(JsonElement root, string key) =>
        root.GetProperty(key)
            .EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetInt32());
}
