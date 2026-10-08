using Microsoft.Extensions.Logging;

namespace Vortex.Observability.Persistence;

/// <summary>How one <see cref="ChannelBatchWriter{TRecord}"/> names itself in the log.</summary>
/// <param name="Kind">What a row is, in a sentence: "durable observability record(s)".</param>
public sealed record BatchWriterEvents(
    string Kind,
    EventId Retry,
    EventId Failed,
    EventId DeadLettered,
    EventId DeadLetterFailed
);
