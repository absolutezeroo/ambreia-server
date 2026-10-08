using System;
using System.Linq;
using FluentAssertions;
using Vortex.Observability.Audit;
using Vortex.Primitives.Observability;
using Xunit;

namespace Vortex.Database.Tests.Observability;

/// <summary>
/// The dead-letter file is where a batch goes exactly when the database is down. A record type the
/// writer persists but cannot write there throws inside the dead-letter write, and the WHOLE batch it
/// sits in is lost.
/// </summary>
public sealed class DurableRecordDeadLetterTests
{
    [Fact]
    public void EveryRecordType_HasADeadLetterRow()
    {
        DurableRecord[] samples =
        [
            new AuditRecord(
                new AuditEvent { Category = AuditCategory.Auth, Action = "auth.login" },
                DateTime.UtcNow,
                null
            ),
            new ItemRecord(
                new ItemForensicEvent { ItemId = 1, EventType = ItemEventType.Placed },
                DateTime.UtcNow,
                null
            ),
        ];

        // A new record type must be added to the samples above, and so to the dead-letter mapper.
        typeof(DurableRecord)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(DurableRecord)) && !t.IsAbstract)
            .Should()
            .BeEquivalentTo(samples.Select(s => s.GetType()));

        foreach (DurableRecord sample in samples)
        {
            AuditWriterService.DeadLetterRow(sample).Should().NotBeNull();
        }
    }
}
