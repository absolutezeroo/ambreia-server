using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Primitives.World.Rules;

/// <summary>
/// The trigger types a module raises at run time (spec Z1 §9): the editor's validator reads these to
/// tell an author which triggers fire. The room's own — EnterZone, LeaveZone, Interact, Timer — need
/// no declaration; any other type nobody raises has no effect.
/// </summary>
public interface IZoneTriggerSource
{
    public IReadOnlyCollection<ZoneTriggerType> Raises { get; }
}
