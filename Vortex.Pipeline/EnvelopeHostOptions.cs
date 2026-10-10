using System;
using System.Threading.Tasks;

namespace Vortex.Pipeline;

public sealed class EnvelopeHostOptions<TEnvelope, TMeta, TContext>
{
    public Func<TEnvelope, TMeta?, Task<TContext>> CreateContextAsync { get; init; } = default!;

    public Func<TContext, bool>? ShouldShortCircuit { get; init; }

    public bool EnableInheritanceDispatch { get; init; } = true;

    public HandlerExecutionMode HandlerMode { get; init; } = HandlerExecutionMode.Parallel;

    public int? MaxHandlerDegreeOfParallelism { get; init; } = null;

    public Action<Exception, object>? OnHandlerActivationError { get; init; }

    public Action<Exception, object>? OnBehaviorActivationError { get; init; }

    public Action<Exception, object>? OnHandlerInvokeError { get; init; }

    public Action<Exception, object>? OnBehaviorInvokeError { get; init; }

    /// <summary>
    /// What a behavior that throws (at activation or while running) means for the envelope. Open
    /// (the default) skips the behavior and the rest of the chain still runs: right for optional
    /// event hooks. Closed drops the envelope after reporting: right for a command pipeline, where a
    /// behavior is a gate (rate limit, account phase) and a gate that cannot decide must refuse.
    /// </summary>
    public bool BehaviorErrorsFailClosed { get; init; }

    /// <summary>
    /// Invoked when an envelope reaches the end of the pipeline with zero registered handlers (after
    /// any behaviors ran). Unset by default — callers for whom "nobody is listening" is a normal,
    /// expected occurrence (e.g. optional domain-event hooks) should leave this null.
    /// </summary>
    public Action<TEnvelope>? OnNoHandlerRegistered { get; init; }
}
