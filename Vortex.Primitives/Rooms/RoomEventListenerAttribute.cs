using System;

namespace Vortex.Primitives.Rooms;

/// <summary>
/// Marks an <see cref="IRoomEventListener" /> that should be built and attached to every room, which
/// is how an assembly outside the core reaches the in-room event stream — clicks, an avatar
/// walking onto a furni, a furni used.
/// <para>
/// The attribute, not the interface, is what gets a listener built: a listener can also be
/// registered by hand on the room's event module, and scanning the interface alone would build a
/// second copy of it per room.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RoomEventListenerAttribute : Attribute;
