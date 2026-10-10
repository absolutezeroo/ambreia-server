using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Primitives.World.Rules;

/// <summary>Trigger types a module raises from elsewhere than a part of the zone's facts (a monster
/// killed, from its simulation).</summary>
public sealed record DeclaredZoneTriggers(IReadOnlyCollection<ZoneTriggerType> Raises)
    : IZoneTriggerSource;
