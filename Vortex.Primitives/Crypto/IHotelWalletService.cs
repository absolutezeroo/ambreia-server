namespace Vortex.Primitives.Crypto;

/// <summary>
/// The address the hotel holds on a player's behalf -- the client's <i>Stardust</i> wallet.
/// </summary>
public interface IHotelWalletService
{
    /// <summary>
    /// Whether the addresses this hands out are backed by real keys. False when no master seed is
    /// configured, in which case <see cref="GetAddress" /> still answers -- see there for why -- but
    /// nothing on a chain will ever be found at what it answers.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// The player's wallet address. Never empty: the client pushes this into its wallet list only
    /// when it is non-empty, and both the Collections and Rewards tabs only leave their loading
    /// state from a message that is itself requested once per wallet. An empty answer hangs both.
    /// </summary>
    public string GetAddress(int playerId);
}
