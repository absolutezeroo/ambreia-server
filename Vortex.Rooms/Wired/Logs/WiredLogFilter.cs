using System;

namespace Vortex.Rooms.Wired.Logs;

/// <summary>
/// Turns the log-dialog filter ordinals the client sends into an enum value, or into "no filter".
/// </summary>
/// <remarks>
/// The wire carries every filter as an int, while <c>WiredLogLevel</c> and <c>WiredLogSource</c>
/// are byte-backed. <c>Enum.IsDefined(Type, object)</c> demands the underlying type exactly, so it
/// threw <c>ArgumentException</c> on every single request and the dialog never returned a page.
/// Casting the int blind instead would be worse than the throw: the cast is unchecked, so 256 would
/// wrap onto 0 and silently filter to Info. Comparing against the declared values is neither, and
/// does not care what the enum is backed by.
/// </remarks>
public static class WiredLogFilter
{
    /// <summary>The matching enum value, or <c>null</c> when the ordinal names none of them —
    /// which is what the client's "all" sentinel (-1) is meant to produce.</summary>
    public static TEnum? FromWire<TEnum>(int value)
        where TEnum : struct, Enum
    {
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (Convert.ToInt32(candidate) == value)
            {
                return candidate;
            }
        }

        return null;
    }
}
