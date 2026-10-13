using Vortex.Primitives.World;
using Vortex.Tests.Support.World;

namespace Vortex.Rpg.Quests.Tests.Support;

/// <summary>The shared minimal zone document, keyed "camp" as the quests name it.</summary>
internal static class ZoneSample
{
    public static readonly ZoneDocument Camp = ZoneSamples.Minimal("camp");
}
