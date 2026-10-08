using System.Threading;

namespace Vortex.Pipeline.Tests.Fixtures;

/// <summary>Tracks the maximum number of handlers observed running concurrently, to verify
/// <see cref="EnvelopeHostOptions{TEnvelope,TMeta,TContext}.MaxHandlerDegreeOfParallelism"/> is
/// actually enforced by the dispatch engine and not just accepted as a no-op setting.</summary>
public sealed class ConcurrencyTracker
{
    private int _current;
    private int _max;

    public int Max => Volatile.Read(ref _max);

    public void Enter()
    {
        int now = Interlocked.Increment(ref _current);

        int observedMax;
        do
        {
            observedMax = Volatile.Read(ref _max);
            if (now <= observedMax)
            {
                break;
            }
        } while (Interlocked.CompareExchange(ref _max, now, observedMax) != observedMax);
    }

    public void Exit() => Interlocked.Decrement(ref _current);
}
