using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vortex.Specs.Model;

namespace Vortex.Specs.Captures;

public sealed class CaptureImportException(string message) : Exception(message);

/// <summary>
/// Reads capture files and turns them into trigger-and-response observations.
/// </summary>
/// <remarks>
/// The schema is documented in <c>docs/habbo-specs/evidence/captures/README.md</c> and is
/// deliberately small, so a G-Earth or proxy export can be converted into it with a short script
/// rather than this importer having to know every tool's format. A capture is the only evidence in
/// this system that can answer "what does the real server do", so the reader is strict: a message
/// that cannot be named is an error, not a silently dropped line.
/// </remarks>
public sealed class CaptureImporter
{
    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public CaptureDocument Read(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), ReaderOptions);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new CaptureImportException($"{path}: the root of a capture must be an object.");
        }

        string id = String(root, "id") ?? Path.GetFileNameWithoutExtension(path);

        if (
            !root.TryGetProperty("messages", out JsonElement messages)
            || messages.ValueKind != JsonValueKind.Array
        )
        {
            throw new CaptureImportException($"{path}: a capture must have a 'messages' array.");
        }

        List<CaptureMessage> parsed = [];
        int index = 0;

        foreach (JsonElement message in messages.EnumerateArray())
        {
            parsed.Add(ReadMessage(path, message, index));
            index++;
        }

        ValidateResponseLinks(path, parsed);

        return new CaptureDocument
        {
            Id = id,
            Source = ParseSource(String(root, "source")),
            Revision = String(root, "revision"),
            RecordedUtc = String(root, "recordedUtc"),
            Note = String(root, "note"),
            Messages = parsed,
            Path = path,
        };
    }

    private static CaptureMessage ReadMessage(string path, JsonElement message, int fallbackIndex)
    {
        string? directionText = String(message, "direction");

        CaptureDirection direction = directionText switch
        {
            "client_to_server" or "outgoing" or "c2s" => CaptureDirection.ClientToServer,
            "server_to_client" or "incoming" or "s2c" => CaptureDirection.ServerToClient,
            _ => throw new CaptureImportException(
                $"{path}: message {fallbackIndex} has direction '{directionText}'; expected "
                    + "'client_to_server' or 'server_to_client'."
            ),
        };

        string? name = String(message, "name");
        int? header =
            message.TryGetProperty("header", out JsonElement headerElement)
            && headerElement.TryGetInt32(out int headerValue)
                ? headerValue
                : null;

        if (name is null && header is null)
        {
            throw new CaptureImportException(
                $"{path}: message {fallbackIndex} has neither a name nor a header id; there is no way "
                    + "to say what it is."
            );
        }

        Dictionary<string, string> fields = new(StringComparer.Ordinal);

        if (
            message.TryGetProperty("fields", out JsonElement fieldsElement)
            && fieldsElement.ValueKind == JsonValueKind.Object
        )
        {
            foreach (JsonProperty field in fieldsElement.EnumerateObject())
            {
                fields[field.Name] = field.Value.ValueKind switch
                {
                    JsonValueKind.String => field.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null => "null",
                    _ => field.Value.GetRawText(),
                };
            }
        }

        return new CaptureMessage
        {
            Index =
                message.TryGetProperty("index", out JsonElement indexElement)
                && indexElement.TryGetInt32(out int explicitIndex)
                    ? explicitIndex
                    : fallbackIndex,
            Direction = direction,
            Name = name,
            Header = header,
            TimestampMs =
                message.TryGetProperty("timestampMs", out JsonElement timestamp)
                && timestamp.TryGetInt64(out long timestampValue)
                    ? timestampValue
                    : null,
            Fields = fields,
            PayloadHex = String(message, "payloadHex"),
            Recipient = ParseRecipient(String(message, "recipient")),
            RespondsTo =
                message.TryGetProperty("respondsTo", out JsonElement respondsTo)
                && respondsTo.TryGetInt32(out int respondsToValue)
                    ? respondsToValue
                    : null,
        };
    }

    /// <summary>
    /// A <c>respondsTo</c> that points at nothing, or at another server message, would silently
    /// drop the answer out of every observation — so it is an error, like every other thing this
    /// reader cannot make sense of.
    /// </summary>
    private static void ValidateResponseLinks(string path, IReadOnlyList<CaptureMessage> messages)
    {
        foreach (CaptureMessage message in messages)
        {
            if (message.RespondsTo is not int answered)
            {
                continue;
            }

            if (message.Direction != CaptureDirection.ServerToClient)
            {
                throw new CaptureImportException(
                    $"{path}: message {message.Index} is a request and cannot answer one; "
                        + "'respondsTo' belongs on a server_to_client message."
                );
            }

            if (
                !messages.Any(m =>
                    m.Index == answered && m.Direction == CaptureDirection.ClientToServer
                )
            )
            {
                throw new CaptureImportException(
                    $"{path}: message {message.Index} answers {answered}, which is not a "
                        + "client_to_server message in this capture."
                );
            }
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static CaptureSource ParseSource(string? text) =>
        text switch
        {
            "official" => CaptureSource.Official,
            "third_party_server" => CaptureSource.ThirdPartyServer,
            "vortex" => CaptureSource.Vortex,
            _ => CaptureSource.Unknown,
        };

    private static Recipient ParseRecipient(string? text) =>
        text switch
        {
            "actor" => Recipient.Actor,
            "room_users" => Recipient.RoomUsers,
            "other_room_users" => Recipient.OtherRoomUsers,
            "target_user" => Recipient.TargetUser,
            "owner" => Recipient.Owner,
            "moderators" => Recipient.Moderators,
            "global" => Recipient.Global,
            _ => Recipient.Unknown,
        };

    /// <summary>
    /// Splits a capture into observations: each client-to-server message and the run of
    /// server-to-client messages that follows it.
    /// </summary>
    /// <remarks>
    /// The boundary is the next client-to-server message. That is an approximation — a server can
    /// push something unprompted mid-run — so a sequence is treated as one observation of what
    /// followed a trigger, and a claim only firms up once several captures show the same run.
    ///
    /// A message that declares <c>respondsTo</c> is attributed to that trigger and to no other,
    /// wherever it landed in the file. Without it, two requests sent before either answer arrives
    /// hand both answers to the second request and leave the first reading as unanswered — and
    /// that reading goes on to generate a behaviour conflict against an official capture that
    /// observed no such thing.
    /// </remarks>
    public IReadOnlyList<CaptureObservation> Observe(
        CaptureDocument capture,
        RevisionRegistry? registry
    )
    {
        List<CaptureObservation> observations = [];
        List<CaptureMessage> ordered = [.. capture.Messages.OrderBy(m => m.Index)];

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Direction != CaptureDirection.ClientToServer)
            {
                continue;
            }

            string? trigger = Resolve(ordered[i], registry, incoming: true);

            if (trigger is null)
            {
                continue;
            }

            List<string> emitted = [];
            bool inRun = true;

            for (int j = i + 1; j < ordered.Count; j++)
            {
                CaptureMessage message = ordered[j];

                if (message.Direction == CaptureDirection.ClientToServer)
                {
                    inRun = false;
                    continue;
                }

                bool answersThis = message.RespondsTo is int answered
                    ? answered == ordered[i].Index
                    : inRun;

                if (!answersThis)
                {
                    continue;
                }

                string? name = Resolve(message, registry, incoming: false);

                if (name is not null)
                {
                    emitted.Add(name);
                }
            }

            observations.Add(
                new CaptureObservation
                {
                    CaptureId = capture.Id,
                    TriggerPacket = trigger,
                    TriggerFields = ordered[i].Fields,
                    TriggerIndex = ordered[i].Index,
                    EmittedPackets = emitted,
                    Authority = capture.Authority,
                    Evidence = new EvidenceRef
                    {
                        Kind = EvidenceKind.Capture,
                        Authority = capture.Authority,
                        Origin = $"capture:{capture.Id}",
                        Source = capture.Path.Replace('\\', '/'),
                        Symbol = $"{trigger}@{ordered[i].Index}",
                        Note =
                            capture.Source == CaptureSource.Unknown
                                ? "capture does not state where it was recorded; treated as unbacked"
                                : capture.Note,
                    },
                }
            );
        }

        return observations;
    }

    /// <summary>
    /// Names a captured message. A capture that carries only header ids is usable exactly when it
    /// declares a revision this workspace has a registry for; otherwise the ids belong to an unknown
    /// build and guessing a name from them would invent evidence.
    /// </summary>
    private static string? Resolve(
        CaptureMessage message,
        RevisionRegistry? registry,
        bool incoming
    )
    {
        if (message.Name is not null)
        {
            return message.Name;
        }

        if (message.Header is null || registry is null)
        {
            return null;
        }

        IReadOnlyDictionary<string, int> table = incoming ? registry.Incoming : registry.Outgoing;

        foreach (
            KeyValuePair<string, int> entry in table.OrderBy(e => e.Key, StringComparer.Ordinal)
        )
        {
            if (entry.Value == message.Header)
            {
                return entry.Key;
            }
        }

        return null;
    }

    /// <summary>Groups observations by trigger and reports whether they agree on the response.</summary>
    public IReadOnlyList<TriggerSummary> Summarize(IEnumerable<CaptureObservation> observations)
    {
        List<TriggerSummary> summaries = [];

        foreach (
            IGrouping<string, CaptureObservation> group in observations
                .GroupBy(o => o.TriggerPacket, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            List<(IReadOnlyList<string>, int)> sequences =
            [
                .. group
                    .GroupBy(o => string.Join(">", o.EmittedPackets), StringComparer.Ordinal)
                    .Select(g => ((IReadOnlyList<string>)g.First().EmittedPackets, g.Count()))
                    .OrderByDescending(pair => pair.Item2)
                    .ThenBy(pair => string.Join(">", pair.Item1), StringComparer.Ordinal),
            ];

            summaries.Add(
                new TriggerSummary
                {
                    TriggerPacket = group.Key,
                    ObservationCount = group.Count(),
                    Sequences = sequences,
                    BestAuthority = group.Min(o => o.Authority),
                }
            );
        }

        return summaries;
    }

    /// <summary>Reads every capture in a directory. A missing directory yields nothing, not an error.</summary>
    public IReadOnlyList<CaptureDocument> ReadAll(string directory, IList<string> problems)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<CaptureDocument> captures = [];

        foreach (
            string file in Directory
                .EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
        )
        {
            try
            {
                captures.Add(Read(file));
            }
            catch (CaptureImportException error)
            {
                problems.Add(error.Message);
            }
            catch (JsonException error)
            {
                problems.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: not valid JSON ({1})",
                        file.Replace('\\', '/'),
                        error.Message
                    )
                );
            }
        }

        return captures;
    }
}
