using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Primitives.Packets;
using DevRevision = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions;

/// <summary>
/// The dev revision under a production table's ids. Nothing is re-mapped by hand: each dev parser is
/// re-keyed and each dev serializer renumbered through the message's name, so a release speaks
/// exactly the messages the dev revision speaks. A table that does not cover them, moves a fixed id,
/// shares an id or leaves the 1..3999 range is refused here, which stops the server from starting.
/// </summary>
public sealed class ReleaseRevision : IRevision
{
    private const int MaxDrawnHeader = 3999;

    public ReleaseRevision(ReleaseTable table)
    {
        Revision = table.Revision;

        Check(table.Revision, "event", table.Events, DevHeaders.Events, "ClientHelloMessageEvent");
        Check(
            table.Revision,
            "composer",
            table.Composers,
            DevHeaders.Composers,
            "DisconnectReasonComposer"
        );

        DevRevision dev = new();
        Dictionary<int, string> eventNames = DevHeaders.Events.ToDictionary(
            e => e.Value,
            e => e.Key
        );
        Dictionary<int, string> composerNames = DevHeaders.Composers.ToDictionary(
            e => e.Value,
            e => e.Key
        );

        Parsers = dev.Parsers.ToDictionary(
            entry => table.Events[eventNames[entry.Key]],
            entry => entry.Value
        );
        Serializers = dev.Serializers.ToDictionary(
            entry => entry.Key,
            entry =>
                (ISerializer)
                    new RenumberedSerializer(
                        entry.Value,
                        table.Composers[composerNames[entry.Value.Header]]
                    )
        );
    }

    private const string ResourcePrefix = "Releases/";

    /// <summary>The newest embedded release; the AMBREIA-yyyyMMddHHmm name sorts chronologically.</summary>
    public static ReleaseRevision LoadLatest()
    {
        Assembly assembly = typeof(ReleaseRevision).Assembly;
        string name =
            LatestReleaseName(assembly.GetManifestResourceNames())
            ?? throw new InvalidOperationException(
                "No release revision: Vortex.Revisions/Releases/ is empty. Draw one with "
                    + "`node scripts/revision-new.mjs`, or run with Vortex:Revisions:Release=false for the dev revision."
            );

        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using StreamReader reader = new(stream);

        return new ReleaseRevision(ReleaseTable.Parse(reader.ReadToEnd()));
    }

    public static string? LatestReleaseName(IEnumerable<string> resourceNames) =>
        resourceNames
            .Where(name =>
                name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal)
            .LastOrDefault();

    public string Revision { get; }

    public IReadOnlyDictionary<int, IParser> Parsers { get; }

    public IReadOnlyDictionary<Type, ISerializer> Serializers { get; }

    private static void Check(
        string revision,
        string kind,
        IReadOnlyDictionary<string, int> table,
        IReadOnlyDictionary<string, int> dev,
        string fixedName
    )
    {
        foreach (string name in dev.Keys)
        {
            if (!table.TryGetValue(name, out int id))
            {
                throw new InvalidOperationException(
                    $"Release {revision}: {kind} {name} has no id. Draw the revision again with scripts/revision-new.mjs."
                );
            }

            if (name == fixedName && id != ReleaseTable.FixedHeader)
            {
                throw new InvalidOperationException(
                    $"Release {revision}: {name} must stay on {ReleaseTable.FixedHeader}, the table has {id}."
                );
            }

            if (name != fixedName && (id < 1 || id > MaxDrawnHeader))
            {
                throw new InvalidOperationException(
                    $"Release {revision}: {kind} {name} is {id}, outside 1..{MaxDrawnHeader}."
                );
            }
        }

        string? shared = table
            .Where(entry => dev.ContainsKey(entry.Key))
            .GroupBy(entry => entry.Value)
            .Where(group => group.Count() > 1)
            .Select(group =>
                string.Join(" and ", group.Select(entry => entry.Key))
                + $" share {kind} id {group.Key}"
            )
            .FirstOrDefault();

        if (shared is not null)
        {
            throw new InvalidOperationException($"Release {revision}: {shared}.");
        }
    }
}
