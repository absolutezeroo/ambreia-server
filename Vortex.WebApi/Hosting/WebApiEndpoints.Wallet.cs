using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Crypto;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Http;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Linking a self-custody wallet to an account, which happens HERE and not in the game client.
/// </summary>
/// <remarks>
/// <para>
/// The client has no interface for it — habbo.com carries <c>/settings/wallet</c> for exactly this
/// reason, and <c>CollectibleWalletAddressesMessageEventParser</c> reads the addresses it produces
/// after the Stardust one.
/// </para>
/// <para>
/// A signature is the only proof accepted. An address a visitor typed proves nothing: anybody can
/// type anybody's, and on a hotel that counts balances that would be a way to claim somebody else's
/// score. The hotel names a nonce, the wallet signs it, and the address recovered from the signature
/// is the one written down.
/// </para>
/// </remarks>
internal static partial class WebApiEndpoints
{
    public sealed record WalletChallengeResponse(string Nonce, string Message);

    public sealed record WalletLinkRequest(string? Address, string? Signature);

    /// <summary>One player's wallets: the hotel's own, which is derived, and the linked ones.</summary>
    /// <param name="RelicHolders">
    /// Which of the addresses above the chain says holds at least one Relic — the Stardust one
    /// included, because the page marks every row.
    ///
    /// <para>
    /// A set rather than a flag carried on each address, so it cannot fall out of step with the
    /// list it describes: two arrays read by index drift the first time one of them is filtered.
    /// </para>
    /// </param>
    public sealed record WalletsResponse(
        string? StardustAddress,
        IReadOnlyList<string> LinkedAddresses,
        IReadOnlyList<string> RelicHolders
    );

    private static void MapWallet(WebApplication app)
    {
        app.MapGet(
                "/api/user/wallets",
                async Task<
                    Results<Ok<WalletsResponse>, UnauthorizedError, NotFound<ApiErrorResponse>>
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    [FromServices] IDbContextFactory<VortexDbContext> dbContexts,
                    [FromServices] IHotelWalletService hotelWallets,
                    [FromServices] IChainService chain,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    await using VortexDbContext db = await dbContexts
                        .CreateDbContextAsync(ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(
                        await ReadWalletsAsync(db, hotelWallets, chain, playerId.Value, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("UserWallets")
            .WithSummary("The player's hotel wallet and any self-custody wallets they linked.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/wallets/challenge",
                Results<Ok<WalletChallengeResponse>, UnauthorizedError> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    [FromServices] WalletLinkChallenges challenges,
                    [FromServices] IOptions<WebApiConfig> options
                ) =>
                {
                    string? sessionId = ctx.SessionId();

                    if (sessionId is null || ctx.AccountId(sessions) is null)
                    {
                        return Unauthorized();
                    }

                    string nonce = challenges.Issue(sessionId);

                    // The message is returned as well as the nonce, because the browser has to sign
                    // the exact bytes the server will verify. Letting the page compose it from the
                    // nonce is how the two drift apart — one trailing newline is enough — and the
                    // failure then reads as "wrong wallet", which sends everyone looking in the
                    // wrong place.
                    return TypedResults.Ok(
                        new WalletChallengeResponse(
                            nonce,
                            WalletLinkChallenges.MessageFor(HotelName(options.Value), nonce)
                        )
                    );
                }
            )
            .WithName("WalletChallenge")
            .WithSummary("A nonce to sign, and the exact message to sign it in.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/wallets",
                async Task<
                    Results<
                        Ok<WalletsResponse>,
                        BadRequest<ApiErrorResponse>,
                        UnauthorizedError,
                        NotFound<ApiErrorResponse>,
                        Conflict<ApiErrorResponse>
                    >
                > (
                    HttpContext ctx,
                    WalletLinkRequest? body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    [FromServices] IDbContextFactory<VortexDbContext> dbContexts,
                    [FromServices] IHotelWalletService hotelWallets,
                    [FromServices] IChainService chain,
                    [FromServices] WalletLinkChallenges challenges,
                    [FromServices] IWalletProof proof,
                    [FromServices] IOptions<WebApiConfig> options,
                    CancellationToken ct
                ) =>
                {
                    string? sessionId = ctx.SessionId();

                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    if (body is null || string.IsNullOrWhiteSpace(body.Signature))
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    string? nonce = sessionId is null ? null : challenges.Consume(sessionId);

                    if (nonce is null)
                    {
                        // Expired, already spent, or never issued — one answer for all three,
                        // because telling them apart tells a guesser which one they got wrong.
                        return TypedResults.BadRequest(new ApiErrorResponse("challenge_expired"));
                    }

                    string? recovered = proof.RecoverSigner(
                        WalletLinkChallenges.MessageFor(HotelName(options.Value), nonce),
                        body.Signature
                    );

                    if (recovered is null)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_signature"));
                    }

                    // The claimed address is checked against the recovered one, never trusted in its
                    // place. It is in the request only so a page can catch a wallet that switched
                    // accounts mid-flow; what gets stored is what the signature proves.
                    if (
                        !string.IsNullOrWhiteSpace(body.Address)
                        && !string.Equals(
                            proof.ToChecksumAddress(body.Address),
                            recovered,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("address_mismatch"));
                    }

                    await using VortexDbContext db = await dbContexts
                        .CreateDbContextAsync(ct)
                        .ConfigureAwait(false);

                    PlayerWalletEntity? existing = await db
                        .PlayerWallets.FirstOrDefaultAsync(row => row.Address == recovered, ct)
                        .ConfigureAwait(false);

                    if (existing is not null)
                    {
                        // Already this player's: nothing to do, and not an error to them. Somebody
                        // else's: refused, because one address counted for two players is the whole
                        // reason that column is unique.
                        return existing.PlayerEntityId == playerId.Value
                            ? TypedResults.Ok(
                                await ReadWalletsAsync(db, hotelWallets, chain, playerId.Value, ct)
                                    .ConfigureAwait(false)
                            )
                            : TypedResults.Conflict(
                                new ApiErrorResponse("address_linked_elsewhere")
                            );
                    }

                    db.PlayerWallets.Add(
                        new PlayerWalletEntity
                        {
                            PlayerEntityId = playerId.Value,
                            Address = recovered,
                        }
                    );

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);

                    return TypedResults.Ok(
                        await ReadWalletsAsync(db, hotelWallets, chain, playerId.Value, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("LinkWallet")
            .WithSummary("Links a wallet by the signature it gave for the issued challenge.")
            .WithTags(TagUser);

        app.MapDelete(
                "/api/user/wallets/{address}",
                async Task<
                    Results<Ok<WalletsResponse>, UnauthorizedError, NotFound<ApiErrorResponse>>
                > (
                    string address,
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    [FromServices] IDbContextFactory<VortexDbContext> dbContexts,
                    [FromServices] IHotelWalletService hotelWallets,
                    [FromServices] IChainService chain,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    await using VortexDbContext db = await dbContexts
                        .CreateDbContextAsync(ct)
                        .ConfigureAwait(false);

                    PlayerWalletEntity? wallet = await db
                        .PlayerWallets.FirstOrDefaultAsync(
                            row => row.PlayerEntityId == playerId.Value && row.Address == address,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (wallet is null)
                    {
                        return TypedResults.NotFound(new ApiErrorResponse("wallet_not_linked"));
                    }

                    // Removed outright rather than soft-deleted. The address column is unique, so a
                    // tombstone would refuse that same wallet forever after — and unlinking is a
                    // player saying "this is not mine any more", which is not a history worth
                    // keeping at the price of never being able to say the opposite again.
                    db.PlayerWallets.Remove(wallet);

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);

                    return TypedResults.Ok(
                        await ReadWalletsAsync(db, hotelWallets, chain, playerId.Value, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("UnlinkWallet")
            .WithSummary("Unlinks a wallet the player had proved.")
            .WithTags(TagUser);
    }

    /// <summary>What the signed message calls this hotel, so the player can see who is asking.</summary>
    private static string HotelName(WebApiConfig config) =>
        string.IsNullOrWhiteSpace(config.SiteName) ? "This hotel" : config.SiteName;

    private static async Task<WalletsResponse> ReadWalletsAsync(
        VortexDbContext db,
        IHotelWalletService hotelWallets,
        IChainService chain,
        int playerId,
        CancellationToken ct
    )
    {
        List<string> linked = await db
            .PlayerWallets.AsNoTracking()
            .Where(row => row.PlayerEntityId == playerId)
            .OrderBy(row => row.Id)
            .Select(row => row.Address)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The Stardust address is derived and never stored, so it is answered alongside the table
        // rather than read from it — see PlayerWalletEntity for why it is not a row.
        string? stardust = hotelWallets.GetAddress(playerId);

        return new WalletsResponse(
            stardust,
            linked,
            await RelicHoldersAsync(chain, [stardust, .. linked], ct).ConfigureAwait(false)
        );
    }

    /// <summary>
    /// Which of <paramref name="addresses" /> hold at least one Relic right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>eth_call</c> per address against a node the hotel runs itself, and a player has one or
    /// two addresses — so this is cheaper than the round trip that carries it. It is asked per
    /// request rather than cached because the answer changes the moment a Relic is transferred, and
    /// a stale mark on this page is the one thing it is there to report.
    /// </para>
    /// <para>
    /// A chain that refuses or is switched off costs the marks and never the page. This is
    /// decoration beside the addresses, and an account's own wallet list must not stop loading
    /// because a node is down.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<string>> RelicHoldersAsync(
        IChainService chain,
        IReadOnlyList<string?> addresses,
        CancellationToken ct
    )
    {
        if (!chain.Enabled)
        {
            return [];
        }

        List<string> holders = [];

        foreach (string? address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            try
            {
                if ((await chain.GetRelicsAsync(address, ct).ConfigureAwait(false)).Count > 0)
                {
                    holders.Add(address);
                }
            }
            catch
            {
                // Unmarked rather than wrongly marked, and the next load asks again.
            }
        }

        return holders;
    }
}
