using System.Collections.Generic;
using Vortex.Primitives.Rooms.Events;

namespace Vortex.Rooms.Wired;

/// <summary>
/// A variable whose value moves with time rather than with a write, so nothing would ever announce
/// it: the room asks it on every wired tick what changed since the last one.
/// </summary>
/// <remarks>
/// The projectile's readings are the case — a tile count that climbs while the furni flies, with no
/// box writing it. Every other variable announces its own change from the write that made it, and
/// has no reason to be here.
/// </remarks>
internal interface IWiredPolledVariable
{
    /// <summary>The changes since the previous poll, ready to go through the room like any
    /// other variable change.</summary>
    IEnumerable<WiredVariableChangedEvent> Poll(long nowMs);
}
