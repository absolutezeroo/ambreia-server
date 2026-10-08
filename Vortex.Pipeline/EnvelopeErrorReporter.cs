using System;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Observability;

namespace Vortex.Pipeline;

/// <summary>
/// How an envelope host reports a failure it survived (a handler that would not activate, a
/// behavior that threw): to the error grouping, with the ambient player, room and correlation, and
/// to the log. The event registry and the message registry both report this way.
/// </summary>
public static class EnvelopeErrorReporter
{
    /// <param name="host">The host's name in the log, e.g. "Event registry".</param>
    /// <param name="source">The failing step, e.g. <c>event-registry.behavior-invoke</c>.</param>
    public static void Report(
        string host,
        Exception ex,
        string source,
        object env,
        IErrorGroupingSink errorGroupingSink,
        IVortexContextAccessor contextAccessor,
        ILogger logger
    )
    {
        IVortexContext? context = contextAccessor.Current;

        try
        {
            errorGroupingSink.Record(
                ex,
                source,
                env.GetType().Name,
                context?.PlayerId,
                context?.RoomId,
                context?.CorrelationId.Value,
                context?.SessionKey
            );

            logger.LogWarning(
                ex,
                "{Host} pipeline failure: {Source}/{Operation}",
                host,
                source,
                env.GetType().Name
            );
        }
        catch (Exception reportEx)
        {
            // The report must never become the failure: the host carries on either way.
            logger.LogError(
                reportEx,
                "{Host} failed to report a pipeline error for {Operation}",
                host,
                env.GetType().Name
            );
        }
    }
}
