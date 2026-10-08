using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vortex.Pipeline;
using Vortex.Primitives;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Events.Registry;

public sealed class EventRegistry(
    IServiceProvider sp,
    IErrorGroupingSink errorGroupingSink,
    IVortexContextAccessor contextAccessor,
    ILogger<EventRegistry> logger
)
    : EnvelopeHost<IEvent, object, EventContext>(
        sp,
        CreateOptions(errorGroupingSink, contextAccessor, logger)
    )
{
    private static EnvelopeHostOptions<IEvent, object, EventContext> CreateOptions(
        IErrorGroupingSink errorGroupingSink,
        IVortexContextAccessor contextAccessor,
        ILogger<EventRegistry> logger
    )
    {
        return new EnvelopeHostOptions<IEvent, object, EventContext>
        {
            CreateContextAsync = (env, session) =>
                Task.FromResult(
                    new EventContext
                    {
                        CorrelationId =
                            contextAccessor.Current?.CorrelationId.Value
                            ?? CorrelationId.None.Value,
                    }
                ),
            ShouldShortCircuit = static ctx => ctx.Cancel,
            EnableInheritanceDispatch = true,
            HandlerMode = HandlerExecutionMode.Parallel,
            MaxHandlerDegreeOfParallelism = null,
            OnHandlerActivationError = (ex, env) =>
                ReportError(
                    ex,
                    "event-registry.activation",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnHandlerInvokeError = (ex, env) =>
                ReportError(
                    ex,
                    "event-registry.invoke",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnBehaviorActivationError = (ex, env) =>
                ReportError(
                    ex,
                    "event-registry.behavior-activation",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnBehaviorInvokeError = (ex, env) =>
                ReportError(
                    ex,
                    "event-registry.behavior-invoke",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
        };
    }

    private static void ReportError(
        Exception ex,
        string source,
        object env,
        IErrorGroupingSink errorGroupingSink,
        IVortexContextAccessor contextAccessor,
        ILogger logger
    ) =>
        EnvelopeErrorReporter.Report(
            "Event registry",
            ex,
            source,
            env,
            errorGroupingSink,
            contextAccessor,
            logger
        );
}
