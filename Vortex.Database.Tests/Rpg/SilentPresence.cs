using Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Tests.Support;

namespace Vortex.Database.Tests.Rpg;

/// <summary>A grain factory whose presence grains swallow what a character pushes, for the
/// character tests that are not about the push.</summary>
internal static class SilentPresence
{
    public static IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerPresenceGrain>(_ => null)
                : null
        );
}
