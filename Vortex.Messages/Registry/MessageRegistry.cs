using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vortex.Logging;
using Vortex.Pipeline;
using Vortex.Primitives;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Messages.Registry;

public sealed class MessageRegistry(
    IServiceProvider sp,
    IErrorGroupingSink errorGroupingSink,
    IVortexContextAccessor contextAccessor,
    ILogger<MessageRegistry> logger
)
    : EnvelopeHost<IMessageEvent, ISessionContext, MessageContext>(
        sp,
        CreateOptions(sp, errorGroupingSink, contextAccessor, logger)
    )
{
    private static EnvelopeHostOptions<
        IMessageEvent,
        ISessionContext,
        MessageContext
    > CreateOptions(
        IServiceProvider serviceProvider,
        IErrorGroupingSink errorGroupingSink,
        IVortexContextAccessor contextAccessor,
        ILogger<MessageRegistry> logger
    )
    {
        return new EnvelopeHostOptions<IMessageEvent, ISessionContext, MessageContext>
        {
            CreateContextAsync = (env, data) =>
            {
                if (data is null)
                {
                    throw new VortexException(VortexErrorCodeEnum.InvalidSession);
                }

                ISessionGateway sessionGateway =
                    serviceProvider.GetRequiredService<ISessionGateway>();
                PlayerId playerId = sessionGateway.GetPlayerId(data.SessionKey);

                // MessageSystem.PublishAsync already resolved the active room for this exact packet
                // (for tracing/metrics) and opened the ambient scope carrying it before invoking this
                // pipeline, so this reuses that value instead of a second GetActiveRoomAsync grain
                // round trip per packet (PERF-01).
                int roomId = playerId > 0 ? (contextAccessor.Current?.RoomId ?? -1) : -1;

                return Task.FromResult(
                    new MessageContext(
                        data,
                        playerId,
                        roomId,
                        sessionGateway.GetAccountId(data.SessionKey)
                    )
                );
            },
            EnableInheritanceDispatch = true,
            HandlerMode = HandlerExecutionMode.Parallel,
            MaxHandlerDegreeOfParallelism = null,
            // A behavior here is a gate (rate limit, account phase, intent limit): one that throws
            // has not said yes, so the packet is refused rather than handled unguarded.
            BehaviorErrorsFailClosed = true,
            OnHandlerActivationError = (ex, env) =>
                ReportError(
                    ex,
                    "message-registry.activation",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnHandlerInvokeError = (ex, env) =>
                ReportError(
                    ex,
                    "message-registry.invoke",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnBehaviorActivationError = (ex, env) =>
                ReportError(
                    ex,
                    "message-registry.behavior-activation",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnBehaviorInvokeError = (ex, env) =>
                ReportError(
                    ex,
                    "message-registry.behavior-invoke",
                    env,
                    errorGroupingSink,
                    contextAccessor,
                    logger
                ),
            OnNoHandlerRegistered = env =>
                logger.LogWarning(
                    "No handler registered for incoming message {MessageType}",
                    env.GetType().Name
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
            "Message registry",
            ex,
            source,
            env,
            errorGroupingSink,
            contextAccessor,
            logger
        );
}
