using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Runtime;
using Vortex.Runtime.AssemblyProcessing;

namespace Vortex.Rooms.Events;

/// <summary>
/// The fifth extension point: an assembly can contribute <see cref="IRoomEventListener" />s and so
/// observe what happens inside a room. It is the only way in: <c>RoomGrain</c> attaches no
/// listener of its own, so without it a plugin could not see a furni use or a state change at all.
/// </summary>
internal sealed class RoomEventListenerFeatureProcessor(
    IRoomEventListenerProvider roomEventListenerProvider,
    ILogger<RoomEventListenerFeatureProcessor> logger
) : IAssemblyFeatureProcessor
{
    private readonly IRoomEventListenerProvider _roomEventListenerProvider =
        roomEventListenerProvider;
    private readonly ILogger _logger = logger;

    public Task<IDisposable> ProcessAsync(
        Assembly asm,
        IServiceProvider sp,
        CancellationToken ct = default
    )
    {
        CompositeDisposable batch = new CompositeDisposable();

        foreach (
            Type? concrete in AssemblyExplorer.FindAssignees(
                asm,
                typeof(IRoomEventListener),
                WarnNonPublic
            )
        )
        {
            if (
                concrete is null
                || concrete.GetCustomAttribute<RoomEventListenerAttribute>(false) is null
            )
            {
                continue;
            }

            Type listenerType = concrete;

            batch.Add(
                _roomEventListenerProvider.RegisterListener(
                    sp,
                    (sp, roomGrain) =>
                        (IRoomEventListener)
                            ActivatorUtilities.CreateInstance(sp, listenerType, roomGrain)
                )
            );
        }

        return Task.FromResult<IDisposable>(batch);
    }

    private void WarnNonPublic(Type concrete)
    {
        if (concrete.GetCustomAttribute<RoomEventListenerAttribute>(false) is null)
        {
            return;
        }

        _logger.LogWarning(
            "{Type} is marked [RoomEventListener] but is not public, so it was not registered and "
                + "will never see a room event. Make the type public.",
            concrete.FullName
        );
    }
}
