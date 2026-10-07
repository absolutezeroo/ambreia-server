using System.Collections.Generic;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>The live side of a published zone: its compiled document and who is expected where.</summary>
public sealed class ZoneRuntimeState(CompiledZone compiled)
{
    public CompiledZone Compiled { get; } = compiled;

    /// <summary>The entry each expected player arrives on, and when the expectation lapses.</summary>
    public Dictionary<PlayerId, (string EntryId, long ExpiresAtMs)> ExpectedArrivals { get; } = [];

    /// <summary>The players in the room, from their arrival step to their exit.</summary>
    public Dictionary<PlayerId, ZoneVisitor> Visitors { get; } = [];
}
