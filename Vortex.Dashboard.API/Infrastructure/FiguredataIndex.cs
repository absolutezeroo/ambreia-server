using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

            // The type lives on the enclosing <settype>, not on the <set>, so it is carried down.
            // A <set> outside one names no type and is skipped rather than guessed at.
            string? type = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "settype")
                {
                    type = null;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Name == "settype")
                {
                    type = reader.GetAttribute("type");
                    continue;
                }

                if (reader.Name != "set" || string.IsNullOrEmpty(type))
                {
                    continue;
                }

                if (!int.TryParse(reader.GetAttribute("id"), out int id))
                {
                    continue;
                }

                sets.Add(
                    new FigureSet(
                        id,
                        type,
                        reader.GetAttribute("club") is string club && club != "0",
                        reader.GetAttribute("gender") ?? "U"
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
