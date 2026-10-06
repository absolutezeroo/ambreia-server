using System;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Rooms.Games;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Providers;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// The game provider a hand-built <c>RoomGrain</c> needs, loaded the way production loads it: the
/// real feature processor scanning the real <c>Vortex.Rooms</c> assembly for <c>[RoomGame]</c>. A
/// harness that registered the games by hand would pass even if the attribute, the processor or the
/// DI wiring were broken — and "the game builds, tests and never runs" is the exact failure the seam
/// exists to stop.
/// <para>
/// It lives here rather than inside one harness because seven test files build the room grain
/// directly, and every one of them needs the same instance.
/// </para>
/// </summary>
internal static class TestRoomGames
{
    public static IRoomGameProvider Provider()
    {
        IServiceProvider services = new EmptyServiceProvider();
        RoomGameProvider provider = new(services);

        // The processor's own work is synchronous (a reflection sweep); the Task it returns is
        // already completed, so there is nothing here to block on.
#pragma warning disable VSTHRD002
        RoomGameFeatureProcessor processor = new(
            provider,
            NullLogger<RoomGameFeatureProcessor>.Instance
        );

        // This assembly too: TestOnlyGame is the one game the tests host now no shipped game is left.
        foreach (
            Assembly assembly in new[]
            {
                typeof(RoomGrain).Assembly,
                typeof(TestRoomGames).Assembly,
            }
        )
        {
            processor.ProcessAsync(assembly, services).GetAwaiter().GetResult();
        }
#pragma warning restore VSTHRD002

        return provider;
    }

    /// <summary>Resolves nothing: the game modules take only their context, so there is nothing for
    /// the container to supply.</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
