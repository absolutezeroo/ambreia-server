using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vortex.Observability.Configuration;

namespace Vortex.Observability.Audit;

/// <summary>
/// Bounded, in-process queue decoupling durable-observability emission (hot path, many producers)
/// from persistence (single background writer). It carries every durable record family — audit
/// and item forensics. Bounded so a database stall can never grow memory without
/// limit; producers never block — a full channel drops the write and is surfaced as a log by the
/// emitting sink.
/// </summary>
public sealed class AuditChannel
{
    private readonly Channel<DurableRecord> _channel;

    public AuditChannel(IOptions<ObservabilityConfig> options)
    {
        int capacity =
            options.Value.AuditChannelCapacity > 0 ? options.Value.AuditChannelCapacity : 10_000;

        _channel = Channel.CreateBounded<DurableRecord>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );
    }

    internal ChannelReader<DurableRecord> Reader => _channel.Reader;

    /// <summary>Non-blocking enqueue. Returns false when the channel is saturated.</summary>
    internal bool TryWrite(DurableRecord record) => _channel.Writer.TryWrite(record);
}
