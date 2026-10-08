namespace Vortex.Primitives.Authentication;

/// <summary>
/// Whom a sign-in ticket names (spec F3a §5.1): a player, who goes straight into the world, or an
/// account, which opens the character screen. At most one of the two is set.
/// </summary>
public sealed record TicketOwner(int PlayerId, int AccountId)
{
    public static TicketOwner None { get; } = new(0, 0);
}
