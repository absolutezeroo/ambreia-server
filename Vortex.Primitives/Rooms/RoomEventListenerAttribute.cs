using System;

namespace Vortex.Primitives.Rooms;

/// <summary>
/// Marks an <see cref="IRoomEventListener" /> that should be built and attached to every room, which
/// is how an assembly outside the core reaches the in-room event stream — chat, clicks, an avatar
/// walking onto a furni, a wired stack firing.
/// <para>
/// The attribute is what separates the two kinds of listener rather than the interface: the
/// scoreboard presenter implements <see cref="IRoomEventListener" /> too and is attached by
/// <c>RoomGrain</c> itself, in a fixed order it depends on. Scanning the interface alone would build
/// a second copy of it per room.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RoomEventListenerAttribute : Attribute;
