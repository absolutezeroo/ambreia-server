using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Vortex.Dashboard.API.Infrastructure;

/// <summary>
/// Every wearable the hotel ships, read from the client's own <c>figuredata.xml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Read from the asset root rather than imported into a table, for the reason the gamedata files
/// are: the file is what the client is actually served, and a copy in the database would be a
/// second truth that goes stale the day somebody swaps the asset pack. It is also small — about a
/// megabyte, three thousand sets — so parsing it once and holding the result costs nothing.
/// </para>
/// <para>
/// Authoring-time only. The engine never sees figuredata: a task compares figure set ids, and an id
/// means the same thing whether or not this file can be read.
/// </para>
/// </remarks>
internal sealed class FiguredataIndex(DashboardAssetUrls assets, ILogger<FiguredataIndex> logger)
{
    private readonly Lock _gate = new();
    private ImmutableArray<FigureSet> _sets = [];
    private DateTime _readAt;
    private string _readFrom = string.Empty;

    /// <summary>Whether the asset root is configured at all.</summary>
    public bool Available => !string.IsNullOrWhiteSpace(assets.LocalRoot);

    /// <summary>
    /// The sets, parsed once and re-read when the file changes underneath us.
    /// </summary>
    /// <remarks>
    /// Empty when the root is unconfigured or the file is missing or malformed — never an
    /// exception. The picker then shows nothing, which is a visible and recoverable state; a throw
    /// here would take out the whole filter editor over an asset pack nobody deployed.
    /// </remarks>
    public ImmutableArray<FigureSet> Sets()
    {
        if (!Available)
        {
            return [];
        }

        string path = Path.Combine(assets.LocalRoot, "gamedata", "figuredata.xml");

        lock (_gate)
        {
            DateTime modified;

            try
            {
                FileInfo file = new(path);

                if (!file.Exists)
                {
                    return [];
                }

                modified = file.LastWriteTimeUtc;
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not stat figuredata at {Path}.", path);
                return [];
            }

            if (_readFrom == path && _readAt == modified)
            {
                return _sets;
            }

            _sets = Parse(path);
            _readFrom = path;
            _readAt = modified;

            return _sets;
        }
    }

    /// <summary>
    /// Reads the sets out of a figuredata document. Never throws: a malformed file is no sets.
    /// </summary>
    /// <remarks>
    /// Entity resolution is off for the reason <c>FiguremapIndex</c> turns it off: nothing here
    /// needs it, and it reads files.
    /// </remarks>
    internal static ImmutableArray<FigureSet> Read(TextReader xml)
    {
        List<FigureSet> sets = [];

        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = true,
            };

            using XmlReader reader = XmlReader.Create(xml, settings);

            // The colours come first in the document, so one pass suffices: by the time a settype
            // names its palette, that palette has been read.
            Dictionary<int, int> paletteColour = [];

            // The type lives on the enclosing <settype>, not on the <set>, so it is carried down.
            // A <set> outside one names no type and is skipped rather than guessed at.
            string? type = null;
            int? colour = null;
            int palette = 0;

            // The colour a <color> element is about, waiting for the hex in its text node. Reading
            // that content inline would consume the element's end tag, and the loop's own Read()
            // would then step over the sibling after it -- half the palette, silently.
            int? pendingColour = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Text)
                {
                    if (pendingColour is int candidate && InBand(reader.Value))
                    {
                        paletteColour.TryAdd(palette, candidate);
                    }

                    pendingColour = null;
                    continue;
                }

                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "settype")
                {
                    type = null;
                    colour = null;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                pendingColour = null;

                switch (reader.Name)
                {
                    case "palette":
                        palette = int.TryParse(reader.GetAttribute("id"), out int id) ? id : 0;
                        continue;

                    case "color":
                        pendingColour = Candidate(reader, palette, paletteColour);
                        continue;

                    case "settype":
                        type = reader.GetAttribute("type");
                        colour =
                            int.TryParse(reader.GetAttribute("paletteid"), out int used)
                            && paletteColour.TryGetValue(used, out int pick)
                                ? pick
                                : null;
                        continue;
                }

                if (reader.Name != "set" || string.IsNullOrEmpty(type))
                {
                    continue;
                }

                if (!int.TryParse(reader.GetAttribute("id"), out int setId))
                {
                    continue;
                }

                sets.Add(
                    new FigureSet(
                        setId,
                        type,
                        reader.GetAttribute("club") is string club && club != "0",
                        reader.GetAttribute("gender") ?? "U",
                        reader.GetAttribute("colorable") == "0" ? null : colour
                    )
                );
            }
        }
        catch (XmlException)
        {
            return [];
        }

        return [.. sets];
    }

    /// <summary>
    /// Keeps this palette's colour, if it is the first worth drawing a garment in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Membership colours are skipped because the picker is not selling anything, and the near-white and
    /// near-black ends of a palette are skipped because they are where the detail goes: palette 3
    /// opens on <c>dddddd</c>, and a shirt drawn in it is the same pale shape the untinted sprite
    /// already was. The first colour inside the band is a mid tone — a skin tone for palette 1, a
    /// hair brown for 2, a mid brown for 3 — which is what makes two garments of the same
    /// silhouette tell each other apart.
    /// </para>
    /// <para>
    /// ponytail: one colour per palette, taken in document order. Per-set colours would be truer,
    /// but figuredata does not name one — a set has no colour of its own, only the one the player
    /// picked — so there is nothing truer to read.
    /// </para>
    /// </remarks>
    private static int? Candidate(XmlReader reader, int palette, Dictionary<int, int> picked) =>
        palette != 0
        && !picked.ContainsKey(palette)
        && reader.GetAttribute("club") is "0"
        && reader.GetAttribute("selectable") is "1"
        && int.TryParse(reader.GetAttribute("id"), out int id)
            ? id
            : null;

    /// <summary>Whether a hex colour is a mid tone, and so worth drawing a garment in.</summary>
    private static bool InBand(string hex) =>
        Luminance(hex.Trim()) is double luminance && luminance >= 0.18 && luminance <= 0.68;

    /// <summary>Perceived brightness of a six-digit hex colour, or 1 when it is not one.</summary>
    private static double Luminance(string hex)
    {
        if (
            hex.Length != 6
            || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
        )
        {
            return 1;
        }

        return (
                (0.299 * ((rgb >> 16) & 0xFF))
                + (0.587 * ((rgb >> 8) & 0xFF))
                + (0.114 * (rgb & 0xFF))
            ) / 255;
    }

    private ImmutableArray<FigureSet> Parse(string path)
    {
        try
        {
            using StreamReader file = new(path);

            return Read(file);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read figuredata at {Path}.", path);

            return [];
        }
    }
}
