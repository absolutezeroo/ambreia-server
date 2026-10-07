using System;
using Microsoft.Extensions.Logging;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Audit;

/// <summary>The word filter's journal, riding the durable audit channel like every other record.</summary>
internal sealed class ChannelWordFilterJournal(
    AuditChannel channel,
    IVortexContextAccessor contextAccessor,
    ILogger<ChannelWordFilterJournal> logger
) : IWordFilterJournal
{
    public void Record(WordFilterHit hit)
    {
        WordFilterHitRecord record = new(
            hit,
            DateTime.UtcNow,
            contextAccessor.Current?.CorrelationId.Value
        );

        if (!channel.TryWrite(record))
        {
            logger.LogWarning(
                VortexEventIds.WordFilterHitDropped,
                "Word filter hit dropped (channel saturated): player {PlayerId} word {WordId}",
                hit.PlayerId,
                hit.WordId
            );
        }
    }
}
