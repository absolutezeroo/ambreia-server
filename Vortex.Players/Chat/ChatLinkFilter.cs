using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;

namespace Vortex.Players.Chat;

/// <summary>
/// Links in a chat text (spec F3c §6): a <c>[prefix:id]</c> token of a declared kind stays when its
/// id exists, up to the configured count, and is removed otherwise. A token of no declared kind, or
/// malformed, is plain text. The last kind declared for a prefix wins.
/// </summary>
internal sealed partial class ChatLinkFilter
{
    private readonly Dictionary<string, IChatLinkKind> _kinds = new(StringComparer.Ordinal);
    private readonly int _maxLinks;

    public ChatLinkFilter(IEnumerable<IChatLinkKind> kinds, int maxLinks)
    {
        foreach (IChatLinkKind kind in kinds)
        {
            _kinds[kind.Prefix] = kind;
        }

        _maxLinks = maxLinks;
    }

    public async ValueTask<string> FilterAsync(string text, CancellationToken ct)
    {
        StringBuilder kept = new();
        int at = 0;
        int links = 0;

        foreach (Match token in Token().Matches(text))
        {
            if (!_kinds.TryGetValue(token.Groups[1].Value, out IChatLinkKind? kind))
            {
                continue;
            }

            kept.Append(text, at, token.Index - at);
            at = token.Index + token.Length;

            if (links < _maxLinks && await kind.ExistsAsync(token.Groups[2].Value, ct))
            {
                kept.Append(token.Value);
                links++;
            }
        }

        return kept.Append(text, at, text.Length - at).ToString();
    }

    [GeneratedRegex(@"\[([a-z]+):([^\[\]\s]+)\]")]
    private static partial Regex Token();
}
