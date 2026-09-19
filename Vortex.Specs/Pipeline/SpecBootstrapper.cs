using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vortex.Specs.Captures;
using Vortex.Specs.Model;
using Vortex.Specs.Persistence;
using Vortex.Specs.Reasoning;
using Vortex.Specs.Sources;
using Vortex.Specs.Yaml;

namespace Vortex.Specs.Pipeline;

public sealed record BootstrapReport
{
    public required int IncomingPackets { get; init; }

    public required int OutgoingPackets { get; init; }

    public required int Features { get; init; }

    public required int Scenarios { get; init; }

    public required int Conflicts { get; init; }

    public required int CriticalUnknowns { get; init; }

    public required int TotalUnknowns { get; init; }

    public required int PlaceholderFieldNames { get; init; }

    public required int TotalFields { get; init; }

    public required IReadOnlyDictionary<Confidence, int> StructureConfidence { get; init; }

    public required int FilesWritten { get; init; }

    public required int FilesUnchanged { get; init; }

    public required IReadOnlyList<SpecWriteResult> Blocked { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    public required IReadOnlyList<string> Problems { get; init; }

    public required IReadOnlyList<string> SourcesScanned { get; init; }

    public required int Captures { get; init; }

    public required int CaptureObservations { get; init; }
}

/// <summary>
/// Writes the whole spec tree and reports what came of it.
/// </summary>
/// <remarks>
/// Every number in the report is counted from what was actually produced. Nothing here is a target
/// or a placeholder: a run against a checkout with no captures says zero captures and leaves the
/// confidence figures showing what that costs, which is the honest picture and the one that makes
/// the case for going and taking some.
/// </remarks>
public sealed class SpecBootstrapper(SpecWorkspace workspace)
{
    private readonly SpecPipeline _pipeline = new(workspace);

    public BootstrapReport Run(bool force = false, Action<string>? progress = null)
    {
        SpecWorld world = _pipeline.Scan(progress);
        List<string> notes = [];
        ResolvedSpecs specs = SpecPipeline.Resolve(world, notes);

        progress?.Invoke("writing specs");
        SpecStore store = new(workspace.OutputRoot);
        SpecPathAllocator paths = new();
        List<SpecWriteResult> results = [];

        foreach (PacketSpec packet in specs.Packets)
        {
            string direction =
                packet.Direction == PacketDirection.Incoming ? "incoming" : "outgoing";

            results.Add(
                store.Write(
                    paths.Allocate(
                        "packets",
                        direction,
                        SpecStore.FileName(packet.Domain),
                        SpecStore.FileName(packet.Name) + ".yaml"
                    ),
                    "packet",
                    SpecWriter.Packet(packet),
                    force
                )
            );
        }

        foreach (FeatureSpec feature in specs.Features)
        {
            results.Add(
                store.Write(
                    paths.Allocate(
                        "features",
                        SpecStore.FileName(feature.Domain),
                        SpecStore.FileName(LocalId(feature.Id)) + ".yaml"
                    ),
                    "feature",
                    SpecWriter.Feature(feature),
                    force
                )
            );
        }

        foreach (
            IGrouping<string, ScenarioSpec> group in specs
                .Scenarios.GroupBy(s => s.FeatureId, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            FeatureSpec? feature = specs.Features.FirstOrDefault(f =>
                string.Equals(f.Id, group.Key, StringComparison.Ordinal)
            );

            results.Add(
                store.Write(
                    paths.Allocate(
                        "scenarios",
                        SpecStore.FileName(feature?.Domain ?? "unsorted"),
                        SpecStore.FileName(LocalId(group.Key)) + ".yaml"
                    ),
                    "scenarios",
                    SpecWriter.Scenarios(group.Key, [.. group]),
                    force
                )
            );
        }

        foreach (ConflictSpec conflict in specs.Conflicts)
        {
            results.Add(
                store.Write(
                    paths.Allocate(
                        "conflicts",
                        SpecStore.FileName(Naming.PacketNaming.SnakeCase(conflict.Kind.ToString())),
                        SpecStore.FileName(conflict.Id) + ".yaml"
                    ),
                    "conflict",
                    SpecWriter.Conflict(conflict),
                    force
                )
            );
        }

        foreach (UnknownSpec unknown in specs.Unknowns)
        {
            results.Add(
                store.Write(
                    paths.Allocate(
                        "unknowns",
                        unknown.Severity.ToString().ToLowerInvariant(),
                        SpecStore.FileName(unknown.Id) + ".yaml"
                    ),
                    "unknown",
                    SpecWriter.Unknown(unknown),
                    force
                )
            );
        }

        foreach (RevisionRegistry registry in specs.Registries)
        {
            results.Add(
                store.Write(
                    // Keyed by origin, not by revision: the emulator and the official client both
                    // describe the same build, and keying by revision silently collapsed the two
                    // tables into one file — losing exactly the comparison they exist for.
                    paths.Allocate("revisions", SpecStore.FileName(registry.Origin) + ".yaml"),
                    "revision",
                    SpecWriter.Registry(registry),
                    force
                )
            );
        }

        results.AddRange(WriteEvidenceSummaries(store, paths, world, force));
        notes.AddRange(paths.Collisions);

        BootstrapReport report = BuildReport(world, specs, results, notes);
        store.WriteText("REPORT.md", RenderReport(report, workspace));
        store.WriteText(Path.Combine("evidence", "captures", "README.md"), CaptureReadme());

        return report;
    }

    private static string LocalId(string featureId)
    {
        int dot = featureId.IndexOf('.', StringComparison.Ordinal);

        return dot < 0 ? featureId : featureId[(dot + 1)..];
    }

    private IEnumerable<SpecWriteResult> WriteEvidenceSummaries(
        SpecStore store,
        SpecPathAllocator paths,
        SpecWorld world,
        bool force
    )
    {
        foreach (Analysis.Client.ClientScan client in world.Clients)
        {
            YamlMapping body = YamlNode
                .Mapping()
                .Set("origin", client.Origin)
                .Set("authority", client.Authority.Wire())
                .Set("targets_same_revision_as_emulator", client.TargetsSameRevision)
                .Set("packets_read", client.Packets.Count)
                .Set(
                    "client_to_server",
                    client.Packets.Count(p => p.Direction == PacketDirection.Incoming)
                )
                .Set(
                    "server_to_client",
                    client.Packets.Count(p => p.Direction == PacketDirection.Outgoing)
                )
                .Set(
                    "named_fields",
                    client.Packets.SelectMany(p => p.Fields).Count(f => f.Name is not null)
                )
                .Set("total_fields", client.Packets.Sum(p => p.Fields.Count));

            body.SetIfPresent("revision", client.Revision);
            body.Set(
                "unresolved",
                YamlNode.Sequence(client.Unresolved.Take(100).Select(u => YamlNode.Scalar(u)))
            );

            yield return store.Write(
                paths.Allocate("evidence", "client", SpecStore.FileName(client.Origin) + ".yaml"),
                "evidence-summary",
                body,
                force
            );
        }

        foreach (Analysis.Reference.ReferenceScan reference in world.References)
        {
            YamlMapping body = YamlNode
                .Mapping()
                .Set("origin", reference.Origin)
                .Set("authority", reference.Authority.Wire())
                .Set("handlers_read", reference.Behaviours.Count)
                .Set("composers_read", reference.Composers.Count)
                .Set("handlers_that_answer", reference.Behaviours.Count(b => b.Outgoing.Count > 0))
                .Set(
                    "unresolved",
                    YamlNode.Sequence(
                        reference.Unresolved.Take(100).Select(u => YamlNode.Scalar(u))
                    )
                );

            yield return store.Write(
                paths.Allocate(
                    "evidence",
                    "references",
                    SpecStore.FileName(reference.Origin) + ".yaml"
                ),
                "evidence-summary",
                body,
                force
            );
        }

        foreach (CaptureDocument capture in world.Captures)
        {
            List<CaptureObservation> observations =
            [
                .. world.Observations.Where(o =>
                    string.Equals(o.CaptureId, capture.Id, StringComparison.Ordinal)
                ),
            ];

            yield return store.Write(
                paths.Allocate(
                    "evidence",
                    "captures",
                    SpecStore.FileName(capture.Id) + ".observations.yaml"
                ),
                "capture-observations",
                SpecWriter.CaptureObservations(capture, observations),
                force
            );
        }
    }

    private BootstrapReport BuildReport(
        SpecWorld world,
        ResolvedSpecs specs,
        IReadOnlyList<SpecWriteResult> results,
        IReadOnlyList<string> notes
    )
    {
        Dictionary<Confidence, int> confidence = [];

        foreach (PacketSpec packet in specs.Packets)
        {
            confidence[packet.StructureConfidence] =
                confidence.GetValueOrDefault(packet.StructureConfidence) + 1;
        }

        List<PacketFieldSpec> allFields = [.. specs.Packets.SelectMany(p => Flatten(p.Fields))];

        return new BootstrapReport
        {
            IncomingPackets = specs.Packets.Count(p => p.Direction == PacketDirection.Incoming),
            OutgoingPackets = specs.Packets.Count(p => p.Direction == PacketDirection.Outgoing),
            Features = specs.Features.Count,
            Scenarios = specs.Scenarios.Count,
            Conflicts = specs.Conflicts.Count,
            CriticalUnknowns = specs.Unknowns.Count(u => u.Severity == UnknownSeverity.Critical),
            TotalUnknowns = specs.Unknowns.Count,
            PlaceholderFieldNames = allFields.Count(f => f.IsPlaceholderName),
            TotalFields = allFields.Count,
            StructureConfidence = confidence,
            FilesWritten = results.Count(r =>
                r.Outcome is SpecWriteOutcome.Created or SpecWriteOutcome.Updated
            ),
            FilesUnchanged = results.Count(r => r.Outcome == SpecWriteOutcome.Unchanged),
            Blocked = [.. results.Where(r => r.Outcome == SpecWriteOutcome.Blocked)],
            Notes = notes,
            Problems = world.Problems,
            SourcesScanned =
            [
                .. workspace.Trees.Select(t =>
                    $"{t.Id} ({Naming.PacketNaming.SnakeCase(t.Kind.ToString())})"
                ),
            ],
            Captures = world.Captures.Count,
            CaptureObservations = world.Observations.Count,
        };
    }

    private static IEnumerable<PacketFieldSpec> Flatten(IReadOnlyList<PacketFieldSpec> fields)
    {
        foreach (PacketFieldSpec field in fields)
        {
            yield return field;

            foreach (PacketFieldSpec child in Flatten(field.Children))
            {
                yield return child;
            }
        }
    }

    public static string RenderReport(BootstrapReport report, SpecWorkspace workspace)
    {
        StringBuilder builder = new();
        builder.Append("# Habbo Specs\n\n");
        builder.Append(
            "Generated by `habbo-spec bootstrap`. Every number below is counted from what the scan\n"
                + "actually produced against the trees listed under Sources.\n\n"
        );

        builder.Append("## Sources scanned\n\n");

        foreach (string source in report.SourcesScanned)
        {
            builder.Append("- ").Append(source).Append('\n');
        }

        builder.Append('\n').Append("## Packets discovered\n\n");
        builder.Append("| Direction | Count |\n|---|---:|\n");
        builder
            .Append("| Incoming (client to server) | ")
            .Append(report.IncomingPackets)
            .Append(" |\n");
        builder
            .Append("| Outgoing (server to client) | ")
            .Append(report.OutgoingPackets)
            .Append(" |\n");

        builder.Append('\n').Append("## Behaviour\n\n");
        builder.Append("| | Count |\n|---|---:|\n");
        builder.Append("| Features | ").Append(report.Features).Append(" |\n");
        builder.Append("| Scenarios | ").Append(report.Scenarios).Append(" |\n");
        builder.Append("| Captures imported | ").Append(report.Captures).Append(" |\n");
        builder
            .Append("| Capture observations | ")
            .Append(report.CaptureObservations)
            .Append(" |\n");

        builder.Append('\n').Append("## Confidence in packet structure\n\n");
        int total = report.IncomingPackets + report.OutgoingPackets;
        builder.Append("| Level | Packets | Share |\n|---|---:|---:|\n");

        foreach (Confidence level in Enum.GetValues<Confidence>().OrderByDescending(c => (int)c))
        {
            int count = report.StructureConfidence.GetValueOrDefault(level);

            if (count == 0)
            {
                continue;
            }

            builder
                .Append("| ")
                .Append(level.Wire())
                .Append(" | ")
                .Append(count)
                .Append(" | ")
                .Append(Percent(count, total))
                .Append(" |\n");
        }

        builder.Append('\n').Append("## Open questions\n\n");
        builder.Append("| | Count |\n|---|---:|\n");
        builder.Append("| Conflicts | ").Append(report.Conflicts).Append(" |\n");
        builder.Append("| Critical unknowns | ").Append(report.CriticalUnknowns).Append(" |\n");
        builder.Append("| Unknowns in total | ").Append(report.TotalUnknowns).Append(" |\n");
        builder
            .Append("| Fields with no attested name | ")
            .Append(report.PlaceholderFieldNames)
            .Append(" of ")
            .Append(report.TotalFields)
            .Append(" |\n");

        if (report.Captures == 0)
        {
            builder.Append(
                "\n> No captures were available to this run. Every behavioural question in this tree is\n"
                    + "> therefore open: the client and the implementations describe what a packet looks like\n"
                    + "> and what other people's servers do with it, and neither can say what Habbo does.\n"
                    + "> See `evidence/captures/README.md` for the format to drop them in.\n"
            );
        }

        builder.Append('\n').Append("## Files\n\n");
        builder.Append("- written: ").Append(report.FilesWritten).Append('\n');
        builder.Append("- unchanged: ").Append(report.FilesUnchanged).Append('\n');
        builder.Append("- blocked by hand edits: ").Append(report.Blocked.Count).Append('\n');

        if (report.Blocked.Count > 0)
        {
            builder.Append('\n');

            foreach (SpecWriteResult blocked in report.Blocked.Take(50))
            {
                builder
                    .Append("  - ")
                    .Append(workspace.Relative(blocked.Path))
                    .Append(": ")
                    .Append(blocked.Detail)
                    .Append('\n');
            }
        }

        if (report.Notes.Count > 0)
        {
            builder.Append('\n').Append("## Coverage the scan bounded\n\n");

            foreach (string note in report.Notes.Take(80))
            {
                builder.Append("- ").Append(note).Append('\n');
            }
        }

        if (report.Problems.Count > 0)
        {
            builder
                .Append('\n')
                .Append("## What the readers could not resolve\n\n")
                .Append(
                    "Listed so the gaps are visible; each one is a piece of the sources this scan\n"
                )
                .Append("did not manage to read.\n\n");

            foreach (string problem in report.Problems.Take(120))
            {
                builder.Append("- ").Append(problem).Append('\n');
            }

            if (report.Problems.Count > 120)
            {
                builder.Append("- ...and ").Append(report.Problems.Count - 120).Append(" more\n");
            }
        }

        return builder.ToString();
    }

    private static string Percent(int count, int total) =>
        total == 0
            ? "0%"
            : ((count * 100.0) / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string CaptureReadme() =>
        """
            # Captures

            A capture is the only evidence in this tree that can answer "what does the real Habbo server
            do". Client code shows what a packet looks like. Reference emulators show what somebody else
            decided to do about it. Neither is Habbo.

            Drop capture files in this directory as `*.json` and run `habbo-spec import-capture <file>`
            or `habbo-spec bootstrap`.

            ## Format

            ```json
            {
              "id": "room-move-furniture-001",
              "source": "official",
              "revision": "WIN63-202607011411-782849652",
              "recordedUtc": "2026-08-21T12:00:00Z",
              "note": "moving a rug one tile, owner of the room",
              "messages": [
                {
                  "index": 0,
                  "direction": "client_to_server",
                  "name": "MoveObject",
                  "header": 1482,
                  "fields": { "object_id": "4021", "x": "7", "y": "3", "rotation": "2" }
                },
                {
                  "index": 1,
                  "direction": "server_to_client",
                  "name": "ObjectUpdate",
                  "recipient": "room_users"
                }
              ]
            }
            ```

            - `source` decides how much the capture is worth. `official` is the only value that settles a
              question; `third_party_server` is evidence about that server; `vortex` is useful for
              differential testing; anything else is treated as unbacked and said so in the specs.
            - `name` or `header` is required on every message. A message carrying only a header id is
              resolved through the revision registry, which needs `revision` to name a build this
              workspace knows.
            - `recipient` is optional and usually absent: one client cannot see what the rest of the room
              received. Capturing the same action from two accounts is what fills it in, and the recipient
              is part of the behaviour.
            - `fields` are optional. Without them a capture still establishes which packets are sent and in
              what order, which is most of what is missing.
            - `respondsTo` is the index of the request a server message answers. Pairing is otherwise
              positional — everything up to the next client message — so two requests sent before either
              answer arrives give both answers to the second one and leave the first reading as
              unanswered. Set it only where the capture really settles it (distinct payloads, a matching
              id); a wrong one invents official behaviour, and an index that is not a `client_to_server`
              message in the same capture is an error.
            """;
}
