using System.Collections.Generic;
using Vortex.Specs.Model;

namespace Vortex.Specs.Captures;

/// <summary>Where a capture was taken, which is what decides how much it is worth.</summary>
public enum CaptureSource
{
    /// <summary>Unstated. Treated as the weakest possible evidence, and said so loudly.</summary>
    Unknown = 0,

    /// <summary>Recorded against an official Habbo server. The only kind that settles a question.</summary>
    Official,

    /// <summary>Recorded against somebody else's private server. Evidence about that server.</summary>
    ThirdPartyServer,

    /// <summary>Recorded against this emulator. Useful for differential testing, never as authority.</summary>
    Vortex,
}

public enum CaptureDirection
{
    ClientToServer,
    ServerToClient,
}

public sealed record CaptureMessage
{
    public required int Index { get; init; }

    public required CaptureDirection Direction { get; init; }

    /// <summary>Symbolic name when the capture tool knew it.</summary>
    public string? Name { get; init; }

    /// <summary>Header id as seen on the wire. Resolved to a name through the revision registry.</summary>
    public int? Header { get; init; }

    public long? TimestampMs { get; init; }

    /// <summary>Decoded fields, when the capture tool decoded them.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>();

    public string? PayloadHex { get; init; }

    /// <summary>
    /// The index of the client-to-server message this one answers, when the capture knows it.
    /// </summary>
    /// <remarks>
    /// Pairing is positional by default, which mis-attributes every answer that arrives after the
    /// client has already spoken again — two requests sent in the same millisecond hand both their
    /// answers to the second one, and the first reads as unanswered. Neither claim is true, and a
    /// prose caveat in the capture's note does not stop the conflict it invents from being
    /// generated. This says what answers what, and only where the capture actually knows.
    /// </remarks>
    public int? RespondsTo { get; init; }

    /// <summary>
    /// Who received it, when the capture was taken from more than one vantage point. Usually absent:
    /// a single-client capture cannot see what the other occupants of a room received.
    /// </summary>
    public Recipient Recipient { get; init; } = Recipient.Unknown;
}

public sealed record CaptureDocument
{
    public required string Id { get; init; }

    public required CaptureSource Source { get; init; }

    public string? Revision { get; init; }

    public string? RecordedUtc { get; init; }

    public string? Note { get; init; }

    public required IReadOnlyList<CaptureMessage> Messages { get; init; }

    /// <summary>The path it was read from, for evidence.</summary>
    public required string Path { get; init; }

    public EvidenceAuthority Authority =>
        Source switch
        {
            CaptureSource.Official => EvidenceAuthority.OfficialCapture,
            CaptureSource.ThirdPartyServer => EvidenceAuthority.ReferenceEmulator,
            CaptureSource.Vortex => EvidenceAuthority.VortexEmulator,
            _ => EvidenceAuthority.Assumption,
        };
}

/// <summary>
/// One trigger and everything the server sent before the client spoke again.
/// </summary>
/// <remarks>
/// This is the shape the capture exists to produce. A list of packets says which messages exist; a
/// trigger with its ordered response says what the server <em>does</em>, which is the part no amount
/// of reading client or emulator code can establish.
/// </remarks>
public sealed record CaptureObservation
{
    public required string CaptureId { get; init; }

    public required string TriggerPacket { get; init; }

    public required IReadOnlyList<string> EmittedPackets { get; init; }

    public IReadOnlyDictionary<string, string> TriggerFields { get; init; } =
        new Dictionary<string, string>();

    public required int TriggerIndex { get; init; }

    public required EvidenceRef Evidence { get; init; }

    public required EvidenceAuthority Authority { get; init; }
}

/// <summary>Every observation of one trigger across every capture, and whether they agree.</summary>
public sealed record TriggerSummary
{
    public required string TriggerPacket { get; init; }

    public required int ObservationCount { get; init; }

    /// <summary>Distinct emitted sequences seen, most frequent first.</summary>
    public required IReadOnlyList<(
        IReadOnlyList<string> Sequence,
        int Count
    )> Sequences { get; init; }

    /// <summary>True when every observation emitted the same packets in the same order.</summary>
    public bool OrderingIsStable => Sequences.Count == 1;

    public required EvidenceAuthority BestAuthority { get; init; }
}
