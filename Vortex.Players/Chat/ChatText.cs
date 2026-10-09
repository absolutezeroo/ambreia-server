using System.Linq;

namespace Vortex.Players.Chat;

/// <summary>The text a player sent, cleaned (spec F3c §5): no control character, no outer space.
/// Nothing else is rewritten.</summary>
internal static class ChatText
{
    public static string Clean(string text) =>
        string.Concat(text.Where(c => !char.IsControl(c))).Trim();
}
