using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Catalogue.Contracts;

/// <summary>
/// Mint VortexCoin to a player's on-chain wallet.
/// </summary>
/// <remarks>
/// <paramref name="Amount" /> is WHOLE COINS, the way an operator thinks about them. The contract
/// counts the smallest unit and the conversion happens server-side, so the number typed here is the
/// number the player sees in their wallet.
/// </remarks>
public sealed record GiveChainCoinRequest(int PlayerId, int Amount, string Reason)
    : IReasonedRequest;
