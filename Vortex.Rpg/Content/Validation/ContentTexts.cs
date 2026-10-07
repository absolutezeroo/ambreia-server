using System;
using System.Collections.Generic;

namespace Vortex.Rpg.Content.Validation;

/// <summary>
/// A text file of the pack (spec Contenu §7): one <c>key=value</c> per line, <c>#</c> starts a
/// comment, blank lines are skipped. BOM and CRLF are tolerated; the last value of a key wins.
/// </summary>
public static class ContentTexts
{
    public static IReadOnlyDictionary<string, string> Parse(string body)
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);

        foreach (string raw in body.TrimStart('﻿').Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int equals = line.IndexOf('=');

            if (line.TrimStart().StartsWith('#') || equals <= 0)
            {
                continue;
            }

            texts[line[..equals].Trim()] = line[(equals + 1)..];
        }

        return texts;
    }
}
