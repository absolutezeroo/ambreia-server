namespace Vortex.Primitives.Rooms.Enums;

/// <summary>
/// The value a signal carries for a dance, and content stores.
/// </summary>
/// <remarks>
/// Spelled rather than numbered, like every other closed fact: a task filtering on <c>4</c> is one
/// nobody can read, and the number is the client's business rather than the content's.
/// </remarks>
public static class AvatarDanceTypes
{
    public static string ToValue(this AvatarDanceType dance) =>
        dance switch
        {
            AvatarDanceType.Dance => "dance",
            AvatarDanceType.PogoMogo => "pogo_mogo",
            AvatarDanceType.DuckFunk => "duck_funk",
            AvatarDanceType.TheRollie => "the_rollie",
            // Stopping is not dancing, and the room never raises a gesture for it.
            _ => "",
        };
}
