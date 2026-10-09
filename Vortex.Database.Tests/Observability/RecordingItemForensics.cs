using System.Collections.Generic;
using Vortex.Primitives.Observability;

namespace Vortex.Database.Tests.Observability;

/// <summary>An <see cref="IItemForensics"/> that keeps what it was handed.</summary>
internal sealed class RecordingItemForensics : IItemForensics
{
    public List<ItemForensicEvent> Recorded { get; } = [];

    public void Record(in ItemForensicEvent itemEvent) => Recorded.Add(itemEvent);
}
