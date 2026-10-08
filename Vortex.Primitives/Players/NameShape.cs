using Vortex.Primitives.Players.Enums;

namespace Vortex.Primitives.Players;

/// <summary>
/// The player-name shape rule, borrowed from the one place that owns it so the HTTP routes, the
/// character screen and the in-game rename cannot drift apart. Uniqueness is still storage's call,
/// not this.
/// </summary>
public static class NameShape
{
    public static bool IsWellFormed(string? name) =>
        NameChangePolicy.Validate(
            name,
            NameChangePolicy.DEFAULT_MIN_LENGTH,
            NameChangePolicy.DEFAULT_MAX_LENGTH
        ) == NameChangeResultCode.Ok;
}
