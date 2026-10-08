using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Vortex.Pipeline.Tests.Fixtures;

/// <summary>Thread-safe call-order/invocation recorder shared (via DI) between the host and the
/// handler/behavior instances it activates for each publish.</summary>
public sealed class Recorder
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public void Record(string entry) => _entries.Enqueue(entry);
}
