using System;
using System.Text.RegularExpressions;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>
/// Where a document lives under content/ (content authoring §3, §5): a document made in the editor
/// takes its kind's folder and its id's last segment; a path given or exported must stay inside its
/// kind's folder, lowercase segments ending in .json, at most 256 characters.
/// </summary>
public static partial class ContentPaths
{
    private const int MaxLength = 256;

    [GeneratedRegex(@"^[a-z0-9_-]+(/[a-z0-9_-]+)*\.json$")]
    private static partial Regex Relative();

    public static string Derive(string kind, string id) =>
        $"{kind}/{id[(id.LastIndexOf('.') + 1)..]}.json";

    public static bool IsSafe(string kind, string path) =>
        path.Length <= MaxLength
        && path.StartsWith(kind + "/", StringComparison.Ordinal)
        && Relative().IsMatch(path);
}
