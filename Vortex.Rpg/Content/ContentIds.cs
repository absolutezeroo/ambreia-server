using System.Text.RegularExpressions;

namespace Vortex.Rpg.Content;

/// <summary>
/// Stable content ids (spec Contenu §4.1): <c>kind.segment[.segment…]</c>, lower case letters, digits
/// and underscores per segment, 64 characters at most. The first segment is the kind. An id never
/// changes: renaming is a new id and a content migration.
/// </summary>
public static partial class ContentIds
{
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z0-9_]+(\\.[a-z0-9_]+)+$")]
    private static partial Regex Shape();

    public static bool IsValid(string id) => id.Length <= MaxLength && Shape().IsMatch(id);

    /// <summary>The kind an id declares: its first segment.</summary>
    public static string KindOf(string id) => id[..id.IndexOf('.')];
}
