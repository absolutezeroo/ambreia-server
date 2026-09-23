using System;
using System.Linq;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Vortex.Observability.Audit;
using Xunit;

namespace Vortex.Hosting.Tests.WordFilter;

/// <summary>
/// Every record the durable channel carries can be dead-lettered.
/// </summary>
/// <remarks>
/// When the database stays down past the retries, the writer serialises the whole batch to a file,
/// and one record type it does not know throws for the batch — ledger and audit rows with it. The
/// word filter's hits were the first type added to the database mapping and not to this one, and
/// they are the most frequent record there is.
/// </remarks>
public sealed class DurableRecordDeadLetterTests
{
    [Fact]
    public void EveryDurableRecordType_HasADeadLetterShape()
    {
        Type[] recordTypes = typeof(DurableRecord)
            .Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && t.IsSubclassOf(typeof(DurableRecord)))
            .ToArray();

        recordTypes.Should().NotBeEmpty();

        foreach (Type type in recordTypes)
        {
            DurableRecord record = (DurableRecord)RuntimeHelpers.GetUninitializedObject(type);

            Action map = () => AuditWriterService.MapDeadLetterPayload(record);

            map.Should().NotThrow(because: $"{type.Name} must survive a dead-letter write");
        }
    }
}
