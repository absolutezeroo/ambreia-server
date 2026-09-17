using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vortex.Primitives.Chain;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The coin contest: who holds the most VortexCoin.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, like the chain routes beside it, and for the same reason: every figure here can be
/// read off the chain by anybody who knows the addresses, and the addresses are public. Putting a
/// session in front of a leaderboard would protect nothing and stop it being a leaderboard.
/// </para>
/// <para>
/// A snapshot, not a live read — see <see cref="IChainLeaderboard" />. The response carries the time
/// it was taken so a page can say so; a standings table that does not admit its age invites the
/// reader to treat it as live, and this one can be five minutes behind.
/// </para>
/// </remarks>
internal static partial class WebApiEndpointsLeaderboard
{
    /// <param name="Balance">
    /// The balance as a DECIMAL STRING of whole coins, e.g. "12.5". Not a number: JSON numbers are
    /// IEEE doubles in every browser, and a balance is up to 78 digits of wei — handing it over as
    /// a number is how the last few digits of somebody's standing quietly become zeroes.
    /// </param>
    /// <param name="BalanceWei">
    /// The same figure exactly as the contract stores it, also a string. The one a caller should
    /// compare or sum with; <paramref name="Balance" /> is for showing.
    /// </param>
    public sealed record LeaderboardEntryResponse(
        int Rank,
        int PlayerId,
        string PlayerName,
        string Address,
        string Balance,
        string BalanceWei
    );

    public sealed record LeaderboardResponse(
        bool Enabled,
        DateTime? TakenAtUtc,
        IReadOnlyList<LeaderboardEntryResponse> Standings
    );

    /// <summary>Eighteen, like every ERC-20 this hotel mints. See <c>VortexCoin.sol</c>.</summary>
    private const int COIN_DECIMALS = 18;

    public static void MapLeaderboard(WebApplication app, string tag)
    {
        app.MapGet(
                "/api/public/leaderboard/coins",
                (
                    [FromServices] IChainLeaderboard leaderboard,
                    [FromServices] IChainService chain
                ) =>
                    TypedResults.Ok(
                        new LeaderboardResponse(
                            chain.Enabled,
                            leaderboard.TakenAtUtc,
                            [
                                .. leaderboard.Standings.Select(
                                    (entry, index) =>
                                        new LeaderboardEntryResponse(
                                            index + 1,
                                            entry.PlayerId,
                                            entry.PlayerName,
                                            entry.Address,
                                            ToWholeCoins(entry.Balance),
                                            entry.Balance.ToString(CultureInfo.InvariantCulture)
                                        )
                                ),
                            ]
                        )
                    )
            )
            .WithName("CoinLeaderboard")
            .WithSummary("Who holds the most VortexCoin, as of the last snapshot.")
            .WithTags(tag);
    }

    /// <summary>
    /// Wei to a readable figure, without floating point anywhere near it.
    /// </summary>
    /// <remarks>
    /// Integer division for the whole part and the remainder for the fraction, then trailing zeroes
    /// trimmed so a round balance reads "40" rather than "40.000000000000000000". Converting through
    /// <c>double</c> instead would be shorter and would lose the low digits of any balance past
    /// fifteen significant figures — which, at eighteen decimals, is every balance.
    /// </remarks>
    private static string ToWholeCoins(BigInteger wei)
    {
        BigInteger unit = BigInteger.Pow(10, COIN_DECIMALS);
        BigInteger whole = BigInteger.DivRem(wei, unit, out BigInteger fraction);

        if (fraction.IsZero)
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        string digits = fraction
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(COIN_DECIMALS, '0')
            .TrimEnd('0');

        return $"{whole.ToString(CultureInfo.InvariantCulture)}.{digits}";
    }
}
